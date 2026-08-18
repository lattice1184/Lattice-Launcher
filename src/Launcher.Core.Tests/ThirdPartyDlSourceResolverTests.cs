using System.Net;
using System.Net.Http;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>第三方下载 GitHub 加速 resolver：release 直链多候选竞速 / 非 GitHub 单候选 / 镜像格式</summary>
public class ThirdPartyDlSourceResolverTests
{
    private const string ReleaseUrl =
        "https://github.com/farion1231/cc-switch/releases/download/v3.16.5/CC-Switch-v3.16.5-Windows.msi";

    private static ThirdPartyDlSourceResolver Resolver => new();

    /// <summary>失败记忆是静态——每个测试前清空，防测试间污染</summary>
    public ThirdPartyDlSourceResolverTests() => ThirdPartyDlSourceResolver.ClearFailures();

    [Fact]
    public void GitHubRelease_ResolvesToOriginalPlusMirrors()
    {
        var list = Resolver.Resolve(ReleaseUrl);
        Assert.Equal(4, list.Count);
        Assert.Equal(ReleaseUrl, list[0]);
        Assert.Equal($"https://ghproxy.net/{ReleaseUrl}", list[1]);
        Assert.Equal($"https://gh-proxy.com/{ReleaseUrl}", list[2]);
        // 黑科技 A：GitHub API 官方直链占位（ghapi:{o}/{r}/{tag}/{name}，下载前换链）
        Assert.Equal("ghapi:farion1231/cc-switch/v3.16.5/CC-Switch-v3.16.5-Windows.msi", list[3]);
    }

    [Fact]
    public void ExpandedAssets_AlsoMirrored()
    {
        var url = "https://github.com/foo/bar/releases/expanded_assets/v1.0/x.zip";
        Assert.Equal(4, Resolver.Resolve(url).Count);
    }

    /// <summary>8-18 失败记忆：镜像失败 → 下一轮排末位（不再每轮白花 8s HEAD 超时）</summary>
    [Fact]
    public void FailedMirror_MovesToLast()
    {
        ThirdPartyDlSourceResolver.MarkFailed($"https://ghproxy.net/{ReleaseUrl}");
        var list = Resolver.Resolve(ReleaseUrl);
        Assert.Equal(ReleaseUrl, list[0]);
        Assert.Equal($"https://gh-proxy.com/{ReleaseUrl}", list[1]);
        Assert.Equal($"https://ghproxy.net/{ReleaseUrl}", list[2]);
    }

    /// <summary>8-18 失败记忆：官方源失败 → 排到镜像之后（官方被墙不再每次先白打）</summary>
    [Fact]
    public void FailedOfficial_MovesBehindMirrors()
    {
        ThirdPartyDlSourceResolver.MarkFailed(ReleaseUrl);
        var list = Resolver.Resolve(ReleaseUrl);
        Assert.Equal($"https://ghproxy.net/{ReleaseUrl}", list[0]);
        Assert.Equal($"https://gh-proxy.com/{ReleaseUrl}", list[1]);
        Assert.Equal(ReleaseUrl, list[2]);
    }

    /// <summary>8-18 失败记忆：清空后恢复原序；非 GitHub URL 标记被忽略（Resolve 不查询）</summary>
    [Fact]
    public void ClearFailures_RestoresOrder_And_NonGitHubIgnored()
    {
        ThirdPartyDlSourceResolver.MarkFailed($"https://ghproxy.net/{ReleaseUrl}");
        ThirdPartyDlSourceResolver.MarkFailed("https://maven.fabricmc.net/lib/x.jar"); // 非 GitHub 不记
        ThirdPartyDlSourceResolver.ClearFailures();
        Assert.Equal(ReleaseUrl, Resolver.Resolve(ReleaseUrl)[0]);
    }

    /// <summary>8-18：archive 打包下载（zip/tar.gz/zipball）也走镜像竞速——此前漏匹配走单候选直连（用户实测失效根因）</summary>
    [Theory]
    [InlineData("https://github.com/deepseek-ai/deepseek-harness/archive/refs/tags/dsh-v0.1.0-rc.7.zip")]
    [InlineData("https://github.com/foo/bar/archive/refs/heads/main.tar.gz")]
    [InlineData("https://github.com/foo/bar/archive/abcdef0.zipball")]
    public void Archive_ResolvesToOriginalPlusMirrors(string url)
    {
        var list = Resolver.Resolve(url);
        // 官方 + 两个镜像；archive 无 ghapi 换链候选
        Assert.Equal(3, list.Count);
        Assert.Equal(url, list[0]);
        Assert.Equal($"https://ghproxy.net/{url}", list[1]);
        Assert.Equal($"https://gh-proxy.com/{url}", list[2]);
    }

    [Theory]
    // 非 GitHub 域
    [InlineData("https://example.com/file.jar")]
    // tag 页面是 HTML 不是文件——不映射
    [InlineData("https://github.com/foo/bar/releases/tag/v1.0")]
    // 非 https 的 github.com 不映射
    [InlineData("http://github.com/foo/bar/releases/download/v1/x.jar")]
    // releases 相关但不是文件直链
    [InlineData("https://github.com/foo/bar/releases")]
    public void NonRelease_SingleCandidate(string url)
    {
        var list = Resolver.Resolve(url);
        Assert.Single(list);
        Assert.Equal(url, list[0]);
    }

    /// <summary>端到端竞速：原 URL 挂 + 镜像活 → 镜像先到先得，下载成功</summary>
    [Fact]
    public async Task OfficialDown_MirrorWins()
    {
        // 8-18：ghapi 换链走真实网络（GitHubApiDirect.Http 静态）——测试注入 500 handler 使换链失败
        // → ghapi 候选剔除 → 竞速只在官方/镜像间进行（否则签名 URL 若换链成功，stub 未路由返回
        // 默认 123456 会赢——测试结果依赖真实网络波动，flaky）
        var origGhapiHttp = GitHubApiDirect.Http;
        GitHubApiDirect.Http = new HttpClient(new FailGhapiHandler());
        try
        {
            var handler = new HostStubHandler();
            handler.RouteBytes("github.com/farion1231/cc-switch/releases/download/v3.16.5/CC-Switch-v3.16.5-Windows.msi", 500, []);
            handler.RouteBytes("gh-proxy.com/https://github.com/farion1231/cc-switch/releases/download/v3.16.5/CC-Switch-v3.16.5-Windows.msi", 500, []);
            handler.RouteBytes("ghproxy.net/https://github.com/farion1231/cc-switch/releases/download/v3.16.5/CC-Switch-v3.16.5-Windows.msi", 200, "MIRROR"u8.ToArray());
            var http = new HttpClient(handler);
            var svc = new DownloadService(http, new ThirdPartyDlSourceResolver(), new DownloadOptions
            {
                MaxSourceAttempts = 1,
                BackoffProvider = _ => TimeSpan.Zero,
            }, Path.GetTempPath(), (_, _) => Task.FromResult(true));
            var dest = Path.Combine(Path.GetTempPath(), $"gh-mirror-{Guid.NewGuid():N}.msi");
            try
            {
                await svc.DownloadFileAsync(ReleaseUrl, dest, null, 6, null, CancellationToken.None);
                Assert.Equal("MIRROR", await File.ReadAllTextAsync(dest));
            }
            finally
            {
                File.Delete(dest);
            }
        }
        finally
        {
            GitHubApiDirect.Http = origGhapiHttp;
            GitHubApiDirect.ClearCacheForTest();
        }
    }

    /// <summary>ghapi 换链失败用：500 全拒（测试不依赖真实网络）</summary>
    private sealed class FailGhapiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

    /// <summary>按 host+path 返回状态/内容（并发竞速多源并行打请求——List.Add 加锁防丢条目）</summary>
    private sealed class HostStubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (int Status, byte[] Body)> _routes = [];
        private readonly object _lock = new();

        public void RouteBytes(string hostPath, int status, byte[] body) => _routes[hostPath] = (status, body);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = $"{request.RequestUri!.Host}{request.RequestUri.AbsolutePath}";
            lock (_lock)
            {
                if (_routes.TryGetValue(key, out var route))
                {
                    return Task.FromResult(route.Status == 200
                        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(route.Body) }
                        : new HttpResponseMessage((HttpStatusCode)route.Status));
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("123456"u8.ToArray()) });
        }
    }
}
