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
        /// <summary>8-19 完整 Uri 列表（含 query——区分带/不带 gameVersion 的两次请求）</summary>
        public readonly List<string> RequestUrls = [];
        private readonly Dictionary<string, (int Status, byte[] Body)> _routes = [];
        private readonly Dictionary<string, (int Status, byte[] Body)> _routesFull = [];

        public void RouteJson(string hostPath, string json) => _routes[hostPath] = (200, Encoding.UTF8.GetBytes(json));
        public void RouteBytes(string hostPath, byte[] body) => _routes[hostPath] = (200, body);
        public void RouteStatus(string hostPath, int status) => _routes[hostPath] = (status, []);
        /// <summary>8-19 按 PathAndQuery 路由（区分带/不带 gameVersion）；带 body（CF 错误 JSON 模拟）</summary>
        public void RouteJsonFull(string pathAndQuery, string json) => _routesFull[pathAndQuery] = (200, Encoding.UTF8.GetBytes(json));
        public void RouteStatusWithBody(string hostPath, int status, string json)
            => _routes[hostPath] = (status, Encoding.UTF8.GetBytes(json));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = $"{request.RequestUri!.Host}{request.RequestUri.AbsolutePath}";
            var full = request.RequestUri!.PathAndQuery;
            var hasKey = request.Headers.TryGetValues("x-api-key", out var values);
            Requests.Add($"{request.Method} {key} x-api-key={(hasKey ? values!.First() : "(none)")}");
            RequestUrls.Add(full);
            if (_routesFull.TryGetValue(full, out var routeFull))
            {
                // 非 200 也带 body——RouteStatusWithBody 场景要读 CF 错误 JSON
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)routeFull.Status)
                    { Content = new ByteArrayContent(routeFull.Body) });
            }
            if (_routes.TryGetValue(key, out var route))
            {
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)route.Status)
                    { Content = new ByteArrayContent(route.Body) });
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
        // 显式空 key = 禁用（不传 null 动态读设置——本机 settings.json 有 key 时测试会误判）
        var svc = new CurseForgeService("", new HttpClient(new CfStubHandler()));
        Assert.False(svc.IsEnabled);
    }

    [Fact]
    public async Task SearchAsync_Disabled_ReturnsNull_NoHttp()
    {
        var handler = new CfStubHandler();
        var svc = new CurseForgeService("", new HttpClient(handler));
        var results = await svc.SearchAsync(ProjectType.Mod);
        Assert.Null(results);
        Assert.Empty(handler.Requests);
    }

    // ---------- key 有效性验证（#212：设置页填入后失焦验证反馈，不打印 key 内容） ----------

    [Fact]
    public async Task ValidateKey_Ok_Valid()
    {
        var handler = new CfStubHandler();
        handler.RouteJson("api.curseforge.com/v1/mods/search", """{"data":[],"pagination":{"totalCount":0}}""");
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var (valid, msg) = await svc.ValidateKeyAsync();

        Assert.True(valid);
        Assert.Contains("有效", msg);
        Assert.DoesNotContain("test-key", msg); // key 永不进结果/日志
    }

    [Fact]
    public async Task ValidateKey_401_Invalid()
    {
        var handler = new CfStubHandler();
        handler.RouteStatus("api.curseforge.com/v1/mods/search", 401);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var (valid, msg) = await svc.ValidateKeyAsync();

        Assert.False(valid);
        Assert.Contains("无效", msg);
        Assert.Contains("401", msg);
    }

    [Fact]
    public async Task ValidateKey_ServerError_ReportsCode()
    {
        var handler = new CfStubHandler();
        handler.RouteStatus("api.curseforge.com/v1/mods/search", 500);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var (valid, msg) = await svc.ValidateKeyAsync();

        Assert.False(valid);
        Assert.Contains("500", msg); // 服务器错误 ≠ key 无效，如实报告
    }

    [Fact]
    public async Task ValidateKey_NoKey_NoRequest()
    {
        var handler = new CfStubHandler();
        var svc = new CurseForgeService("", new HttpClient(handler)); // 显式禁用

        var (valid, msg) = await svc.ValidateKeyAsync();

        Assert.False(valid);
        Assert.Contains("未配置", msg);
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

        Assert.NotNull(results);
        Assert.Single(results!.Projects);
        Assert.Equal("Sodium", results.Projects[0].name);
        Assert.Equal(12345, results.Projects[0].downloadCount);
        Assert.Equal("jellysquid3", results.Projects[0].authors[0].name);
        Assert.Equal(1, results.TotalCount); // 无分页信息 → 回退当前页条数
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

        // 防幂等残留：上一轮运行可能已在共享 mods 留下同名文件（sha1 匹配会跳过下载 → 请求断言失败）
        var stale = Path.Combine(Path.GetTempPath(), "mods", "sodium-0.5.11.jar");
        if (File.Exists(stale)) File.Delete(stale);

        var dest = await svc.InstallAsync(100, file, $"1.21.1-{Guid.NewGuid():N}", ProjectType.Mod);

        Assert.True(File.Exists(dest));
        Assert.Equal(content, await File.ReadAllBytesAsync(dest));
        Assert.Contains(Path.Combine("mods", "sodium-0.5.11.jar"), dest);
        Assert.Contains(handler.Requests, r => r.Contains("cdn.example.com/files/sodium-0.5.11.jar"));
    }
    // ---------- 8-19 容错 + 版本参数降级 ----------

    private const string CfErrorJson = "{\"statusCode\":400,\"error\":\"Bad Request\",\"message\":\"Invalid game version parameter\"}";

    [Fact]
    public async Task SearchAsync_ErrorJsonBody_ThrowsCfApiExceptionWithMessage()
    {
        var handler = new CfStubHandler();
        handler.RouteJson("api.curseforge.com/v1/mods/search", CfErrorJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<CurseForgeService.CurseForgeApiException>(() =>
            svc.SearchAsync(ProjectType.Shader, gameVersion: "26.2"));
        Assert.Equal(400, ex.CfStatusCode);
        Assert.Contains("Invalid game version parameter", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_InvalidGameVersion_DowngradesOnceWithoutVersion()
    {
        var handler = new CfStubHandler();
        handler.RouteJsonFull("/v1/mods/search?gameId=432&classId=6552&gameVersion=26.2&sortField=1&sortOrder=desc&index=0&pageSize=20", CfErrorJson);
        handler.RouteJson("api.curseforge.com/v1/mods/search", ProjectJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var page = await svc.SearchAsync(ProjectType.Shader, gameVersion: "26.2");

        Assert.NotNull(page);
        Assert.True(page.VersionFilterDropped);
        Assert.Equal(1, page.Projects.Count);
        Assert.Equal(2, handler.RequestUrls.Count);                     // 带版本 + 不带版本各一次
        Assert.Contains("gameVersion=26.2", handler.RequestUrls[0]);
        Assert.DoesNotContain("gameVersion", handler.RequestUrls[1]);
    }

    [Fact]
    public async Task SearchAsync_DowngradeFailsSecondTime_ThrowsAndExactlyTwoRequests()
    {
        var handler = new CfStubHandler();
        handler.RouteJson("api.curseforge.com/v1/mods/search", CfErrorJson);   // 两种 URL 都命中同一条路由（host+path 匹配）
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        await Assert.ThrowsAsync<CurseForgeService.CurseForgeApiException>(() =>
            svc.SearchAsync(ProjectType.Shader, gameVersion: "26.2"));
        Assert.Equal(2, handler.RequestUrls.Count);          // 防循环：最多 2 请求
    }

    [Fact]
    public async Task SearchAsync_Html200Body_ThrowsGenericMessage()
    {
        var handler = new CfStubHandler();
        handler.RouteBytes("api.curseforge.com/v1/mods/search", "<html>CloudFront error</html>"u8.ToArray());
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            svc.SearchAsync(ProjectType.Shader, gameVersion: "26.2"));
        Assert.Contains("响应格式异常", ex.Message);
    }

    [Fact]
    public async Task GetJsonAsync_Non2xx400_WithCfErrorBody_ThrowsCfApiException()
    {
        var handler = new CfStubHandler();
        handler.RouteStatusWithBody("api.curseforge.com/v1/mods/search", 400, CfErrorJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<CurseForgeService.CurseForgeApiException>(() =>
            svc.SearchAsync(ProjectType.Shader, gameVersion: "1.21.1"));
        Assert.Equal(400, ex.CfStatusCode);
        Assert.Contains("Invalid game version parameter", ex.Message);
    }

    [Fact]
    public async Task GetFilesAsync_InvalidGameVersion_FallsBackToAllFiles()
    {
        var handler = new CfStubHandler();
        handler.RouteStatusWithBody("api.curseforge.com/v1/mods/100/files", 400, CfErrorJson);   // 带 gameVersion 的 files 请求
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50", FilesJson); // 不带版本的请求
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var files = await svc.GetFilesAsync(100, "26.2");

        Assert.Single(files);
        Assert.Equal(2, handler.RequestUrls.Count);
        Assert.DoesNotContain("gameVersion", handler.RequestUrls[1]);
    }

    [Fact]
    public async Task FindBestFileAsync_Dropped_SelectsFromUnfilteredPool()
    {
        var handler = new CfStubHandler();
        handler.RouteStatusWithBody("api.curseforge.com/v1/mods/100/files", 400, CfErrorJson);
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50", FilesJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var best = await svc.FindBestFileAsync(100, "26.2");

        Assert.NotNull(best); // 降级后从全池选——不再按 26.2 过滤（否则误报「没有适配文件」）
        Assert.Equal("Sodium 0.5.11", best.displayName);
    }

    [Fact]
    public async Task SearchAsync_NoGameVersion_NoFallback()
    {
        var handler = new CfStubHandler();
        handler.RouteJson("api.curseforge.com/v1/mods/search", ProjectJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var page = await svc.SearchAsync(ProjectType.Shader);

        Assert.NotNull(page);
        Assert.False(page.VersionFilterDropped);
        Assert.Single(handler.RequestUrls); // 无版本 → 不降级重试
    }

    // ---------- 8-19 补：CF 对无效版本返回 200+空（非 400）——空结果也降级 ----------

    [Fact]
    public async Task GetFilesAsync_Empty200_InvalidVersion_FallsBackToAllFiles()
    {
        var handler = new CfStubHandler();
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50&gameVersion=26.2", """{"data":[]}""");
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50", FilesJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var files = await svc.GetFilesAsync(100, "26.2");

        Assert.Single(files); // 200+空也降级——26.2 实测 CF files 返回空而非 400
        Assert.Equal(2, handler.RequestUrls.Count);
        Assert.DoesNotContain("gameVersion", handler.RequestUrls[1]);
    }

    [Fact]
    public async Task FindBestFileAsync_Empty200_InvalidVersion_SelectsFromUnfilteredPool()
    {
        var handler = new CfStubHandler();
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50&gameVersion=26.2", """{"data":[]}""");
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50", FilesJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var best = await svc.FindBestFileAsync(100, "26.2");

        Assert.NotNull(best); // 降级后从全池选——不再误报「没有适配文件」
        Assert.Equal("Sodium 0.5.11", best.displayName);
    }

    [Fact]
    public async Task SearchAsync_Empty200_NoQuery_DowngradesAndFlags()
    {
        var handler = new CfStubHandler();
        handler.RouteJsonFull("/v1/mods/search?gameId=432&classId=6552&gameVersion=26.2&sortField=1&sortOrder=desc&index=0&pageSize=20", """{"data":[],"pagination":{"totalCount":0}}""");
        handler.RouteJson("api.curseforge.com/v1/mods/search", ProjectJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var page = await svc.SearchAsync(ProjectType.Shader, gameVersion: "26.2");

        Assert.NotNull(page);
        Assert.True(page.VersionFilterDropped);
        Assert.Single(page.Projects);
        Assert.Equal(2, handler.RequestUrls.Count);
    }

    [Fact]
    public async Task SearchAsync_Empty200_WithQuery_NoDowngrade()
    {
        // 带搜索词 0 结果大概率词不匹配——不降级（否则状态栏误导「版本不支持过滤」）
        var handler = new CfStubHandler();
        handler.RouteJson("api.curseforge.com/v1/mods/search", """{"data":[],"pagination":{"totalCount":0}}""");
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var page = await svc.SearchAsync(ProjectType.Shader, query: "fabric-loader-0.19.3-26.2 fabric", gameVersion: "26.2");

        Assert.NotNull(page);
        Assert.False(page.VersionFilterDropped);
        Assert.Empty(page.Projects);
        Assert.Single(handler.RequestUrls); // 不降级：恰 1 请求
    }

    // ---------- 8-19 补 2：GetFilesWithFallbackAsync（VM 详情页用 dropped 感知版本） ----------

    [Fact]
    public async Task GetFilesWithFallbackAsync_26_2_ReturnsAllAndDroppedTrue()
    {
        var handler = new CfStubHandler();
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50&gameVersion=26.2", """{"data":[]}""");
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50", FilesJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var (files, dropped) = await svc.GetFilesWithFallbackAsync(100, "26.2", default);

        Assert.True(dropped);   // 年份号空 → 降级全量
        Assert.Single(files);
        Assert.Equal(2, handler.RequestUrls.Count);
        Assert.DoesNotContain("gameVersion", handler.RequestUrls[1]);
    }

    [Fact]
    public async Task GetFilesWithFallbackAsync_TraditionalVersion_FilteredAndDroppedFalse()
    {
        var handler = new CfStubHandler();
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50&gameVersion=1.21.1", FilesJson);
        var svc = new CurseForgeService("test-key", new HttpClient(handler));

        var (files, dropped) = await svc.GetFilesWithFallbackAsync(100, "1.21.1", default);

        Assert.False(dropped);      // 传统版本正常过滤：不降级
        Assert.Single(files);
        Assert.Single(handler.RequestUrls); // 恰 1 请求
    }

}
