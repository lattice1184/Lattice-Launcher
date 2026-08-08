using System.Net;
using Launcher.Core.Download;
using Launcher.Core.Model.Mojang;
using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>AL29 H6：安装后完整性校验——下载完成必须 == 文件完整，不得「虚假成功」</summary>
public class VersionInstallerTests
{
    private static VersionJson BuildVersion(params LibraryJson[] libs)
        => new("1.21.11", "release", "net.minecraft.client.main.Main",
            null, null, null, null, [.. libs],
            new DownloadsInfo(new DownloadFileInfo("http://test/c/client.jar", null, 5), null, null),
            null, null, null);

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("12345"), // 5 字节，匹配 size=5
            });
    }

    private static (VersionInstaller installer, string gameDir) MakeInstaller()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"vinst-{Guid.NewGuid():N}");
        var http = new HttpClient(new StubHandler());
        // 与 MirrorFallbackTests 同款 stub 链：stub 处理器 + 单候选解析器（任意 host 都按官方源）
        // + 零退避 + 跳过真实网络预检——否则 stub 主机 DNS 失败会走真实镜像网络（13 秒重试才报错）。
        // 注意：必须注入 DownloadService，VersionInstaller 自建的会带真实 HttpClient + 真实网络检查器。
        var resolver = new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper());
        var downloads = new DownloadService(http, resolver, new DownloadOptions
        {
            MaxSourceAttempts = 2,
            BackoffProvider = _ => TimeSpan.Zero,
        }, gameDir, (_, _) => Task.FromResult(true));
        return (new VersionInstaller(gameDirectory: gameDir, downloads: downloads), gameDir);
    }

    [Fact]
    public async Task Install_CompleteFiles_Succeeds_AndMarks()
    {
        var (installer, gameDir) = MakeInstaller();
        var lib = new LibraryJson("net.x:ok:1.0", null, null, null,
            new LibraryDownloads(new DownloadFileInfo("http://test/libs/ok.jar", null, 5), null), null, null, null);
        var version = BuildVersion(lib);

        await installer.InstallAsync(version, (DownloadProgressHandler?)null, CancellationToken.None);

        // client jar + 库都落地，标记写入，不抛
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "1.21.11", "1.21.11.jar")));
        Assert.True(File.Exists(Path.Combine(gameDir, "libraries", "net", "x", "ok", "1.0", "ok-1.0.jar")));
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "1.21.11", ".yanla-installed")));
    }

    [Fact]
    public async Task Install_SkippedLibrary_ThrowsAfterDownload()
    {
        var (installer, gameDir) = MakeInstaller();
        var ok = new LibraryJson("net.x:ok:1.0", null, null, null,
            new LibraryDownloads(new DownloadFileInfo("http://test/libs/ok.jar", null, 5), null), null, null, null);
        // 无 url 无 downloads 的库：下载器合法跳过（AL10.2 曾发生的 url 形式库问题场景）
        var missing = new LibraryJson("net.x:missing:1.0", null, null, null, null, null, null, null);
        var version = BuildVersion(ok, missing);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.InstallAsync(version, (DownloadProgressHandler?)null, CancellationToken.None));

        Assert.Contains("缺 1 个文件", ex.Message);
        Assert.Contains("missing-1.0.jar", ex.Message);
    }

    [Fact]
    public async Task Install_Failure_CleansClientJar_AndSkipsMark()
    {
        // 事务化安装：下载失败 → 清理本次新建的 client jar + 不打完整安装标记——
        // 半装态消失后「已安装」判定（json+jar）恢复诚实，不再"显示已安装→启动才报缺文件"
        var gameDir = Path.Combine(Path.GetTempPath(), $"vinst-{Guid.NewGuid():N}");
        try
        {
            var resolver = new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper());
            var downloads = new DownloadService(new HttpClient(new FailHandler()), resolver, new DownloadOptions
            {
                MaxSourceAttempts = 2,
                BackoffProvider = _ => TimeSpan.Zero,
            }, gameDir, (_, _) => Task.FromResult(true));
            var installer = new VersionInstaller(gameDirectory: gameDir, downloads: downloads);

            await Assert.ThrowsAnyAsync<Exception>(
                () => installer.InstallAsync(BuildVersion(), (DownloadProgressHandler?)null, CancellationToken.None));

            var vdir = Path.Combine(gameDir, "versions", "1.21.11");
            Assert.False(File.Exists(Path.Combine(vdir, "1.21.11.jar")), "失败后 client jar 必须被清理");
            Assert.False(File.Exists(Path.Combine(vdir, ".yanla-installed")), "失败不得带完整安装标记");
        }
        finally
        {
            if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true);
        }
    }

    /// <summary>全 404——下载必然失败（换源也 404）</summary>
    private sealed class FailHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
