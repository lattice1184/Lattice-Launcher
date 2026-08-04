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
             "downloads":{"server":{"url":"https://piston-data.mojang.com/v1/objects/abc/server.jar","sha1":"s","size":5}}}
            """);
        return dir;
    }

    /// <summary>清理整个临时根（含父级 servers 目录）</summary>
    private static void CleanUp(string gameDir)
    {
        var root = Path.GetDirectoryName(gameDir);
        if (root is not null && Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public async Task InstallAsync_OfficialFails_FallsBackToBmclapi()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("piston-data.mojang.com/v1/objects/abc/server.jar", 500, []);
        handler.RouteBytes("bmclapi2.bangbang93.com/version/1.21.10/server", 200, "12345"u8.ToArray());
        var installer = new ServerInstaller(CreateService(handler));
        var dir = MakeGameDir();
        try
        {
            var jar = await installer.InstallAsync("1.21.10", dir);

            Assert.True(File.Exists(jar));
            Assert.Equal("12345", File.ReadAllText(jar));
            Assert.Contains(handler.Requests, r => r.Contains("piston-data.mojang.com"));
            Assert.Contains(handler.Requests, r => r.Contains("bmclapi2.bangbang93.com/version/1.21.10/server"));
        }
        finally { CleanUp(dir); }
    }

    [Fact]
    public async Task InstallAsync_OfficialOk_NoMirrorRequest()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("piston-data.mojang.com/v1/objects/abc/server.jar", 200, "12345"u8.ToArray());
        var installer = new ServerInstaller(CreateService(handler));
        var dir = MakeGameDir();
        try
        {
            var jar = await installer.InstallAsync("1.21.10", dir);

            Assert.True(File.Exists(jar));
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
        handler.RouteBytes("launcher.mojang.com/v1/objects/abc/server.jar", 200, "12345"u8.ToArray());
        var installer = new ServerInstaller(CreateService(handler));
        var dir = MakeGameDir();
        try
        {
            var jar = await installer.InstallAsync("1.21.10", dir);

            Assert.True(File.Exists(jar));
            Assert.Equal("12345", File.ReadAllText(jar));
            Assert.Contains(handler.Requests, r => r.Contains("launcher.mojang.com/v1/objects/abc/server.jar"));
            Assert.DoesNotContain(handler.Requests, r => r.Contains("bmclapi2.bangbang93.com"));
        }
        finally { CleanUp(dir); }
    }
}
