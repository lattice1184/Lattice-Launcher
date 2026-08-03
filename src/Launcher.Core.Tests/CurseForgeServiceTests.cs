using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Launcher.Core.Download;
using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;
using Launcher.Core.Utils;
using PCL.Core.Minecraft.ResourceProject.Curseforge;

namespace Launcher.Core.Tests;

/// <summary>CurseForge 服务：key 解析 / 搜索 URL / 文件选择 / 安装（stub 网络）</summary>
public class CurseForgeServiceTests
{
    /// <summary>按 host+path 路由 JSON/字节；记录请求（含 x-api-key 头）</summary>
    private sealed class CfStubHandler : HttpMessageHandler
    {
        public readonly List<string> Requests = [];
        private readonly Dictionary<string, (int Status, byte[] Body)> _routes = [];

        public void RouteJson(string hostPath, string json) => _routes[hostPath] = (200, Encoding.UTF8.GetBytes(json));
        public void RouteBytes(string hostPath, byte[] body) => _routes[hostPath] = (200, body);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = $"{request.RequestUri!.Host}{request.RequestUri.AbsolutePath}";
            var hasKey = request.Headers.TryGetValues("x-api-key", out var values);
            Requests.Add($"{request.Method} {key} x-api-key={(hasKey ? values!.First() : "(none)")}");
            if (_routes.TryGetValue(key, out var route))
            {
                return Task.FromResult(route.Status == 200
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(route.Body) }
                    : new HttpResponseMessage((HttpStatusCode)route.Status));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const string ProjectJson = """
        {"data":[{"id":100,"gameId":432,"name":"Sodium","slug":"sodium","links":null,"summary":"Fast renderer",
        "status":1,"downloadCount":12345,"isFeatured":false,"primaryCategoryId":1,"categories":[],
        "classId":6,"authors":[{"id":1,"name":"jellysquid3","url":""}],"logo":null,"screenshots":[],
        "mainFileId":0,"latestFiles":[]}]}
        """;

    private const string FilesJson = """
        {"data":[{"id":7,"gameId":432,"modId":100,"isAvailable":true,"displayName":"Sodium 0.5.11","fileName":"sodium-0.5.11.jar",
        "releaseType":1,"fileStatus":1,"hashes":{"value":"abc","algo":1},
        "downloadUrl":"https://cdn.example.com/files/sodium-0.5.11.jar","fileLength":1234,
        "gameVersions":["1.21.1","1.21.4"],"dependencies":[{"modId":200,"relationType":1}]}]}
        """;

    // ---------- key 解析 ----------

    [Fact]
    public void ResolveApiKey_SettingsWins_ThenEnv_ThenEmpty()
    {
        Assert.Equal("settings-key",
            CurseForgeService.ResolveApiKey(new LauncherSettings { CurseForgeApiKey = "settings-key" }));

        var prev = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", "env-key");
            Assert.Equal("env-key", CurseForgeService.ResolveApiKey(new LauncherSettings()));
        }
        finally { Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", prev); }
    }

    [Fact]
    public void IsEnabled_FalseWhenNoKey()
    {
        var svc = new CurseForgeService((string?)null, new HttpClient(new CfStubHandler()));
        Assert.False(svc.IsEnabled);
    }

    [Fact]
    public async Task SearchAsync_Disabled_ReturnsEmpty_NoHttp()
    {
        var handler = new CfStubHandler();
        var svc = new CurseForgeService((string?)null, new HttpClient(handler));
        var results = await svc.SearchAsync(ProjectType.Mod);
        Assert.Empty(results);
        Assert.Empty(handler.Requests);
    }

    // ---------- 静态工具 ----------

    [Theory]
    [InlineData(ProjectType.Modpack, 4471)]
    [InlineData(ProjectType.Resourcepack, 12)]
    [InlineData(ProjectType.Shader, 6552)]
    [InlineData(ProjectType.Mod, 6)]
    public void ClassIdFor_Maps(ProjectType type, int expected)
        => Assert.Equal(expected, CurseForgeService.ClassIdFor(type));

    [Theory]
    [InlineData(CurseForgeService.SortIndex.Relevance, 1)]
    [InlineData(CurseForgeService.SortIndex.Downloads, 6)]
    [InlineData(CurseForgeService.SortIndex.Newest, 11)]
    [InlineData(CurseForgeService.SortIndex.Updated, 3)]
    public void SortFieldFor_Maps(CurseForgeService.SortIndex sort, int expected)
        => Assert.Equal(expected, CurseForgeService.SortFieldFor(sort));

    [Fact]
    public void BuildSearchUrl_FullParams()
    {
        var url = CurseForgeService.BuildSearchUrl(ProjectType.Mod, "sodium", "1.21.1",
            CurseForgeService.SortIndex.Downloads, 20, 0);
        Assert.Contains("gameId=432", url);
        Assert.Contains("classId=6", url);
        Assert.Contains("searchFilter=sodium", url);
        Assert.Contains("gameVersion=1.21.1", url);
        Assert.Contains("sortField=6", url);
        Assert.Contains("sortOrder=desc", url);
        Assert.Contains("pageSize=20", url);
    }

    // ---------- 文件选择 ----------

    private static CurseforgeFile CfFile(int id, int releaseType, bool available, List<string>? versions = null)
        => new(id, 432, 100, available, $"f{id}", $"f{id}.jar", releaseType, 1,
            new CurseforgeHashes($"h{id}", 1), $"https://cdn.example.com/f{id}.jar", id * 10, versions, null);

    [Fact]
    public void SelectBestFile_ReleasePreferred_FileIdDesc()
    {
        var files = new List<CurseforgeFile>
        {
            CfFile(1, 2, true),   // beta
            CfFile(2, 1, true),   // release
            CfFile(3, 1, false),  // release but unavailable
        };
        var best = CurseForgeService.SelectBestFile(files, null);
        Assert.Equal(2, best!.id);
    }

    [Fact]
    public void SelectBestFile_GameVersionFilter()
    {
        var files = new List<CurseforgeFile>
        {
            CfFile(1, 1, true, ["1.20.4"]),
            CfFile(2, 1, true, ["1.21.1"]),
            CfFile(3, 1, true, null), // 版本未知 → 放行
        };
        var best = CurseForgeService.SelectBestFile(files, "1.21.1");
        Assert.Equal(3, best!.id); // 未知版本优先（fail-open），其次 1.21.1
    }

    // ---------- 网络（stub） ----------

    [Fact]
    public async Task SearchAsync_ParsesData_SendsApiKey()
    {
        var handler = new CfStubHandler();
        handler.RouteJson("api.curseforge.com/v1/mods/search", ProjectJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var results = await svc.SearchAsync(ProjectType.Mod, "sodium");

        Assert.Single(results);
        Assert.Equal("Sodium", results[0].name);
        Assert.Equal(12345, results[0].downloadCount);
        Assert.Equal("jellysquid3", results[0].authors[0].name);
        Assert.Contains("x-api-key=test-key", handler.Requests.Single());
    }

    [Fact]
    public async Task GetFilesAsync_ParsesNewFields()
    {
        var handler = new CfStubHandler();
        handler.RouteJson("api.curseforge.com/v1/mods/100/files", FilesJson);
        var svc = new CurseForgeService("k", new HttpClient(handler));

        var files = await svc.GetFilesAsync(100);

        var file = Assert.Single(files);
        Assert.Equal("https://cdn.example.com/files/sodium-0.5.11.jar", file.downloadUrl);
        Assert.Equal(1234, file.fileLength);
        Assert.Contains("1.21.1", file.gameVersions!);
        Assert.Equal(200, file.dependencies![0].modId);
        Assert.Equal(1, file.dependencies![0].relationType);
    }

    [Fact]
    public async Task InstallAsync_DownloadsToModsDir_WithSha1()
    {
        var content = "fake jar content"u8.ToArray();
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(content));
        var handler = new CfStubHandler();
        handler.RouteBytes("cdn.example.com/files/sodium-0.5.11.jar", content);
        var downloads = new DownloadService(new HttpClient(handler),
            new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper()),
            new DownloadOptions { MaxSourceAttempts = 2, BackoffProvider = _ => TimeSpan.Zero },
            Path.GetTempPath());
        var svc = new CurseForgeService("k", new HttpClient(handler), downloads, Path.GetTempPath());
        var file = new CurseforgeFile(7, 432, 100, true, "Sodium", "sodium-0.5.11.jar", 1, 1,
            new CurseforgeHashes(sha1, 1), "https://cdn.example.com/files/sodium-0.5.11.jar",
            content.Length, ["1.21.1"], null);

        var dest = await svc.InstallAsync(100, file, $"1.21.1-{Guid.NewGuid():N}", ProjectType.Mod);

        Assert.True(File.Exists(dest));
        Assert.Equal(content, await File.ReadAllBytesAsync(dest));
        Assert.Contains(Path.Combine("mods", "sodium-0.5.11.jar"), dest);
        Assert.Contains(handler.Requests, r => r.Contains("cdn.example.com/files/sodium-0.5.11.jar"));
    }
}
