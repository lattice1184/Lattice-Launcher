using System.Net;
using System.Net.Http;
using Launcher.Core.Download;
using Launcher.Core.Server;

namespace Launcher.Core.Tests;

/// <summary>
/// 服务端安装：官方源（piston-data）失败 → BMCLAPI 镜像兜底（AL 批次——"下载服务端基本失败"根因：
/// 服务端 jar 无镜像映射，官方直连国内不稳即失败）。
/// </summary>
public class ServerInstallerTests
{
    /// <summary>按 host+path 返回状态/内容；跟踪请求序列（与 MirrorFallbackTests 同款）</summary>
    private sealed class HostStubHandler : HttpMessageHandler
    {
        public readonly List<string> Requests = [];
        private readonly Dictionary<string, (int Status, byte[] Body)> _routes = [];

        public void RouteBytes(string hostPath, int status, byte[] body) => _routes[hostPath] = (status, body);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = $"{request.RequestUri!.Host}{request.RequestUri.AbsolutePath}";
            Requests.Add($"{request.Method} {key}");
            if (_routes.TryGetValue(key, out var route))
            {
                return Task.FromResult(route.Status == 200
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(route.Body) }
                    : new HttpResponseMessage((HttpStatusCode)route.Status));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static DownloadService CreateService(HostStubHandler handler) => new(
        new HttpClient(handler),
        new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper()),
        new DownloadOptions { MirrorFallbackEnabled = true, MaxSourceAttempts = 2, BackoffProvider = _ => TimeSpan.Zero },
        Path.GetTempPath(),
        (_, _) => Task.FromResult(true)); // 跳过真实网络预检——全走 stub（测试不依赖外网）

    /// <summary>临时 gameDir + 版本 json（带 downloads.server.url，指向官方 piston-data）。
    /// gameDir 放 {Temp}/srvtest-{GUID}/.minecraft——ServerDir 取 gameDir 父级/servers/{id}，保证 servers 也在隔离目录内</summary>
    private static string MakeGameDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"srvtest-{Guid.NewGuid():N}");
        var dir = Path.Combine(root, ".minecraft");
        var vdir = Path.Combine(dir, "versions", "1.21.10");
        Directory.CreateDirectory(vdir);
        File.WriteAllText(Path.Combine(vdir, "1.21.10.json"), """
            {"id":"1.21.10","type":"release","mainClass":"net.minecraft.server.Main",
             "downloads":{"server":{"url":"https://piston-data.mojang.com/v1/objects/abc/server.jar","sha1":"s"}}}
            """);
        return dir;
    }

    /// <summary>清理整个临时根（含父级 servers 目录）</summary>
    private static void CleanUp(string gameDir)
    {
        var root = Path.GetDirectoryName(gameDir);
        if (root is not null && Directory.Exists(root)) Directory.Delete(root, true);
    }

    /// <summary>有效服务端 jar 内容（≥1MB + zip 魔数 PK）——通过 ServerInstaller 的 IsValidServerJar 校验</summary>
    private static byte[] FakeJar()
    {
        var b = new byte[1024 * 1024 + 16];
        b[0] = 0x50; b[1] = 0x4B;
        return b;
    }

    [Fact]
    public async Task InstallAsync_OfficialFails_FallsBackToBmclapi()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("piston-data.mojang.com/v1/objects/abc/server.jar", 500, []);
        handler.RouteBytes("bmclapi2.bangbang93.com/version/1.21.10/server", 200, FakeJar());
        var installer = new ServerInstaller(CreateService(handler));
        var dir = MakeGameDir();
        try
        {
            var jar = await installer.InstallAsync("1.21.10", dir);

            Assert.True(File.Exists(jar));
            Assert.True(new FileInfo(jar).Length >= 1024 * 1024);
            Assert.Contains(handler.Requests, r => r.Contains("piston-data.mojang.com"));
            Assert.Contains(handler.Requests, r => r.Contains("bmclapi2.bangbang93.com/version/1.21.10/server"));
        }
        finally { CleanUp(dir); }
    }

    [Fact]
    public async Task InstallAsync_OfficialOk_NoMirrorRequest()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("piston-data.mojang.com/v1/objects/abc/server.jar", 200, FakeJar());
        var installer = new ServerInstaller(CreateService(handler));
        var dir = MakeGameDir();
        try
        {
            var jar = await installer.InstallAsync("1.21.10", dir);

            Assert.True(File.Exists(jar));
            Assert.True(new FileInfo(jar).Length >= 1024 * 1024);
            Assert.DoesNotContain(handler.Requests, r => r.Contains("bmclapi2.bangbang93.com"));
        }
        finally { CleanUp(dir); }
    }

    /// <summary>AL2：官方 piston-data 失败 → launcher.mojang.com 旧域名（不等到 BMCLAPI 才成功）</summary>
    [Fact]
    public async Task InstallAsync_PistonDataFails_LauncherDomainSucceeds()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("piston-data.mojang.com/v1/objects/abc/server.jar", 500, []);
        handler.RouteBytes("launcher.mojang.com/v1/objects/abc/server.jar", 200, FakeJar());
        var installer = new ServerInstaller(CreateService(handler));
        var dir = MakeGameDir();
        try
        {
            var jar = await installer.InstallAsync("1.21.10", dir);

            Assert.True(File.Exists(jar));
            Assert.True(new FileInfo(jar).Length >= 1024 * 1024);
            Assert.Contains(handler.Requests, r => r.Contains("launcher.mojang.com/v1/objects/abc/server.jar"));
            Assert.DoesNotContain(handler.Requests, r => r.Contains("bmclapi2.bangbang93.com"));
        }
        finally { CleanUp(dir); }
    }

    /// <summary>AL3：候选返回 200 错误内容（如 BMCLAPI WAF 挑战页）→ 校验拒绝、删除、继续下一候选</summary>
    [Fact]
    public async Task InstallAsync_InvalidSmallContent_SkipsToNextCandidate()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("piston-data.mojang.com/v1/objects/abc/server.jar", 500, []);
        handler.RouteBytes("launcher.mojang.com/v1/objects/abc/server.jar", 200,
            "<html>Just a moment... challenge</html>"u8.ToArray()); // 200 错误页（过小）
        handler.RouteBytes("bmclapi2.bangbang93.com/version/1.21.10/server", 200, FakeJar());
        var installer = new ServerInstaller(CreateService(handler));
        var dir = MakeGameDir();
        try
        {
            var jar = await installer.InstallAsync("1.21.10", dir);

            Assert.True(File.Exists(jar));
            Assert.True(new FileInfo(jar).Length >= 1024 * 1024);
            Assert.Contains(handler.Requests, r => r.Contains("launcher.mojang.com"));
            Assert.Contains(handler.Requests, r => r.Contains("bmclapi2.bangbang93.com/version/1.21.10/server"));
        }
        finally { CleanUp(dir); }
    }

    // ---------- AM：服务端 URL 自动推断（整合包/加载器版本无 downloads.server） ----------

    /// <summary>stub 清单/版本 json 路由（manifest + 版本 json 都指向 piston-meta）</summary>
    private static void RouteManifest(HostStubHandler handler, string mcVersion, string serverPath)
    {
        const string manifest = """
            {"latest":{"release":"26.2"},"versions":[
              {"id":"1.21.1","type":"release","url":"https://piston-meta.mojang.com/v1/packages/aaa/1.21.1.json"},
              {"id":"1.21.10","type":"release","url":"https://piston-meta.mojang.com/v1/packages/bbb/1.21.10.json"}
            ]}
            """;
        handler.RouteBytes("piston-meta.mojang.com/mc/game/version_manifest_v2.json", 200, System.Text.Encoding.UTF8.GetBytes(manifest));
        handler.RouteBytes($"piston-meta.mojang.com/v1/packages/{mcVersion switch { "1.21.1" => "aaa", _ => "bbb" }}/{mcVersion}.json", 200,
            System.Text.Encoding.UTF8.GetBytes(
                $"{{\"id\":\"{mcVersion}\",\"downloads\":{{\"server\":{{\"url\":\"https://piston-data.mojang.com/v1/objects/abc/server.jar\"}}}}}}"));
    }

    /// <summary>版本 json（无 downloads.server，id 带数字前缀如 1.21.1-Fabric 0.19.3）→ 前缀推断 → 清单拿链接</summary>
    private static string MakeLoaderGameDir(string id = "1.21.1-Fabric 0.19.3")
    {
        var root = Path.Combine(Path.GetTempPath(), $"srvtest-{Guid.NewGuid():N}");
        var dir = Path.Combine(root, ".minecraft");
        var vdir = Path.Combine(dir, "versions", id);
        Directory.CreateDirectory(vdir);
        File.WriteAllText(Path.Combine(vdir, $"{id}.json"),
            $$"""{"id":"{{id}}","type":"release","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[]}""");
        return dir;
    }

    [Fact]
    public async Task InstallAsync_LoaderVersion_InfersMcFromIdPrefix()
    {
        var handler = new HostStubHandler();
        RouteManifest(handler, "1.21.1", "abc");
        handler.RouteBytes("piston-data.mojang.com/v1/objects/abc/server.jar", 200, FakeJar());
        var installer = new ServerInstaller(CreateService(handler), new HttpClient(handler));
        var dir = MakeLoaderGameDir();
        try
        {
            var jar = await installer.InstallAsync("1.21.1-Fabric 0.19.3", dir);

            Assert.True(File.Exists(jar));
            Assert.True(new FileInfo(jar).Length >= 1024 * 1024);
            Assert.Contains(handler.Requests, r => r.Contains("version_manifest_v2.json")); // 走了清单推断
            Assert.Contains(handler.Requests, r => r.Contains("piston-data.mojang.com/v1/objects/abc/server.jar"));
        }
        finally { CleanUp(dir); }
    }

    /// <summary>整合包版本（id 无数字前缀、无 downloads.server）→ jar 内 version.json 推断 MC 版本</summary>
    [Fact]
    public async Task InstallAsync_ModpackVersion_InfersMcFromJarVersionJson()
    {
        var handler = new HostStubHandler();
        RouteManifest(handler, "1.21.10", "abc");
        handler.RouteBytes("piston-data.mojang.com/v1/objects/abc/server.jar", 200, FakeJar());
        var installer = new ServerInstaller(CreateService(handler), new HttpClient(handler));
        var root = Path.Combine(Path.GetTempPath(), $"srvtest-{Guid.NewGuid():N}");
        var dir = Path.Combine(root, ".minecraft");
        var vdir = Path.Combine(dir, "versions", "红石生电优化");
        Directory.CreateDirectory(vdir);
        try
        {
            File.WriteAllText(Path.Combine(vdir, "红石生电优化.json"),
                """{"id":"红石生电优化","type":"release","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[]}""");
            // 整合包 jar = 原版 client jar 改名：内含 version.json {"id":"1.21.10"}
            using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(vdir, "红石生电优化.jar"), System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("version.json");
                using var w = new StreamWriter(entry.Open());
                w.Write("""{"id":"1.21.10","name":"1.21.10"}""");
            }

            var jar = await installer.InstallAsync("红石生电优化", dir);

            Assert.True(File.Exists(jar));
            Assert.True(new FileInfo(jar).Length >= 1024 * 1024);
            Assert.Contains(handler.Requests, r => r.Contains("bbb/1.21.10.json")); // jar 推断出 1.21.10
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>清单中找不到该 MC 版本 → 明确报错</summary>
    [Fact]
    public async Task InstallAsync_ManifestMissingVersion_Throws()
    {
        var handler = new HostStubHandler();
        RouteManifest(handler, "1.21.1", "abc"); // manifest 只有 1.21.1/1.21.10
        var installer = new ServerInstaller(CreateService(handler), new HttpClient(handler));
        var dir = MakeLoaderGameDir("9.9.9-Fabric 0.19.3"); // 前缀推断 9.9.9——manifest 里没有
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => installer.InstallAsync("9.9.9-Fabric 0.19.3", dir));
            Assert.Contains("9.9.9", ex.Message);
        }
        finally { CleanUp(dir); }
    }
}
