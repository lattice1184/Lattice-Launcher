using System.Net;
using System.Net.Http;
using Launcher.Core.Download;
using Launcher.Core.Model.Loader;

namespace Launcher.Core.Tests;

/// <summary>加载器下载源：四家 meta 解析 / 计划构造 / Fabric 直装落盘 / 前置条件（离线 StubHttp）</summary>
public class LoaderServiceTests
{
    private const string FabricProfileJson = """
        {"id":"fabric-loader-0.16.13-1.21.1","inheritsFrom":"1.21.1","type":"release",
         "mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient",
         "libraries":[{"name":"net.fabricmc:fabric-loader:0.16.13",
                       "url":"https://maven.fabricmc.net/",
                       "downloads":{"artifact":{"url":"https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.16.13/fabric-loader-0.16.13.jar","size":5}}}]}
        """;

    private static LoaderService CreateService(Dictionary<string, string> routes, string gameDir)
    {
        var http = new HttpClient(new StubHandler(routes));
        var downloads = new DownloadService(http, gameDirectory: gameDir);
        return new LoaderService(http, downloads, gameDir);
    }

    [Fact]
    public async Task FabricMeta_StableNewestFirst()
    {
        var routes = new Dictionary<string, string>
        {
            ["/v2/versions/loader/1.21.1"] = """
                [{"loader":{"version":"0.19.3","stable":true}},
                 {"loader":{"version":"0.16.13","stable":true}},
                 {"loader":{"version":"0.14.22","stable":false}}]
                """,
        };
        var svc = CreateService(routes, Path.GetTempPath());

        var versions = await svc.GetLoaderVersionsAsync(LoaderKind.Fabric, "1.21.1", CancellationToken.None);

        Assert.Equal(3, versions.Count);
        Assert.Equal("0.19.3", versions[0].Version);
        Assert.True(versions[0].IsStable);
        Assert.False(versions[2].IsStable);
    }

    [Fact]
    public async Task QuiltMeta_BetaDetected()
    {
        var routes = new Dictionary<string, string>
        {
            ["/v3/versions/loader/1.21.1"] = """
                [{"loader":{"version":"0.20.0-beta.9"}},{"loader":{"version":"0.19.1"}}]
                """,
        };
        var svc = CreateService(routes, Path.GetTempPath());

        var versions = await svc.GetLoaderVersionsAsync(LoaderKind.Quilt, "1.21.1", CancellationToken.None);

        Assert.Equal(2, versions.Count);
        Assert.False(versions[0].IsStable); // -beta.9
        Assert.True(versions[1].IsStable);  // 0.19.1
    }

    [Fact]
    public async Task ForgePromos_RecommendedFirst()
    {
        var routes = new Dictionary<string, string>
        {
            ["/net/minecraftforge/forge/promotions_slim.json"] =
                """{"promos":{"1.21.1-recommended":"52.1.0","1.21.1-latest":"52.1.16"}}""",
        };
        var svc = CreateService(routes, Path.GetTempPath());

        var versions = await svc.GetLoaderVersionsAsync(LoaderKind.Forge, "1.21.1", CancellationToken.None);

        Assert.Equal(2, versions.Count);
        Assert.Equal("52.1.0", versions[0].Version);
        Assert.True(versions[0].IsStable);
        Assert.Equal("52.1.16", versions[1].Version);
    }

    [Fact]
    public async Task NeoForgeMeta_PrefixFilteredAndNumericSorted()
    {
        var routes = new Dictionary<string, string>
        {
            ["/releases/net/neoforged/neoforge/maven-metadata.xml"] = """
                <metadata><versioning><versions>
                  <version>21.1.99</version>
                  <version>21.1.110</version>
                  <version>26.2.0.41-beta</version>
                </versions></versioning></metadata>
                """,
        };
        var svc = CreateService(routes, Path.GetTempPath());

        var versions = await svc.GetLoaderVersionsAsync(LoaderKind.NeoForge, "1.21.1", CancellationToken.None);

        Assert.Equal(2, versions.Count); // 26.x 不属于 21.1. 前缀
        Assert.Equal("21.1.110", versions[0].Version); // 数字比较 110 > 99
        Assert.Equal("21.1.99", versions[1].Version);
    }

    [Fact]
    public async Task CreatePlan_UrlsConstructedForAllKinds()
    {
        // 显式传 loaderVersion → 不触网，纯 URL 构造
        var svc = CreateService([], Path.GetTempPath());

        var fabric = await svc.CreatePlanAsync(LoaderKind.Fabric, "1.21.1", "0.16.13", CancellationToken.None);
        Assert.Equal("https://meta.fabricmc.net/v2/versions/loader/1.21.1/0.16.13/profile/json", fabric.ProfileJsonUrl);

        var quilt = await svc.CreatePlanAsync(LoaderKind.Quilt, "1.21.1", "0.20.0-beta.9", CancellationToken.None);
        Assert.Equal("https://meta.quiltmc.org/v3/versions/loader/1.21.1/0.20.0-beta.9/profile/json", quilt.ProfileJsonUrl);

        var forge = await svc.CreatePlanAsync(LoaderKind.Forge, "1.21.1", "52.1.0", CancellationToken.None);
        Assert.Equal("https://maven.minecraftforge.net/net/minecraftforge/forge/1.21.1-52.1.0/forge-1.21.1-52.1.0-installer.jar", forge.InstallerUrl);

        var neo = await svc.CreatePlanAsync(LoaderKind.NeoForge, "1.21.1", "21.1.110", CancellationToken.None);
        Assert.Equal("https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.110/neoforge-21.1.110-installer.jar", neo.InstallerUrl);
    }

    [Fact]
    public async Task FabricInstall_WritesProfileAndDownloadsChain()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"loader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gameDir, "versions", "1.21.1"));
        try
        {
            // 父版本（原版已安装）：含 downloads.client
            File.WriteAllText(Path.Combine(gameDir, "versions", "1.21.1", "1.21.1.json"),
                """{"id":"1.21.1","mainClass":"net.minecraft.client.main.Main","libraries":[],"downloads":{"client":{"url":"https://piston/1.21.1/client.jar","size":5}}}""");

            var routes = new Dictionary<string, string>
            {
                ["/v2/versions/loader/1.21.1"] = """[{"loader":{"version":"0.16.13","stable":true}}]""",
                ["/v2/versions/loader/1.21.1/0.16.13/profile/json"] = FabricProfileJson,
            };
            var svc = CreateService(routes, gameDir);

            var plan = await svc.CreatePlanAsync(LoaderKind.Fabric, "1.21.1", "0.16.13", CancellationToken.None);
            await svc.InstallAsync(plan, (DownloadProgressHandler?)null, CancellationToken.None);

            // profile json 落盘（含继承关系）
            var id = "fabric-loader-0.16.13-1.21.1";
            var versionDir = Path.Combine(gameDir, "versions", id);
            Assert.True(File.Exists(Path.Combine(versionDir, $"{id}.json")));
            // 链解析后 client jar 落在子版本目录
            Assert.True(File.Exists(Path.Combine(versionDir, $"{id}.jar")), "client jar 应沿 inheritsFrom 链下载到子版本");
            // 加载器库落盘
            Assert.True(File.Exists(Path.Combine(gameDir, "libraries", "net", "fabricmc", "fabric-loader", "0.16.13", "fabric-loader-0.16.13.jar")));
        }
        finally { Directory.Delete(gameDir, true); }
    }

    [Fact]
    public async Task FabricInstall_MissingVanilla_Throws()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"loader-{Guid.NewGuid():N}");
        try
        {
            var routes = new Dictionary<string, string>
            {
                ["/v2/versions/loader/1.21.1"] = """[{"loader":{"version":"0.16.13","stable":true}}]""",
                ["/v2/versions/loader/1.21.1/0.16.13/profile/json"] = FabricProfileJson,
            };
            var svc = CreateService(routes, gameDir);

            var plan = await svc.CreatePlanAsync(LoaderKind.Fabric, "1.21.1", "0.16.13", CancellationToken.None);
            var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => svc.InstallAsync(plan, (DownloadProgressHandler?)null, CancellationToken.None));

            Assert.Contains("1.21.1", ex.Message);
        }
        finally { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); }
    }

    /// <summary>按路径返回预设响应；未匹配路径返回 5 字节假文件内容（下载用）</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes;

        public StubHandler(Dictionary<string, string> routes) => _routes = routes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = _routes.TryGetValue(path, out var json) ? json : "12345"; // 5 字节，匹配 size=5 校验
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8),
            });
        }
    }
}
