using System.Net;
using System.Net.Http;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>GitHub API 官方直链换链（黑科技 A）：两步 API → 签名直链；失败返回 null 不影响竞速</summary>
public class GitHubApiDirectTests
{
    private const string SignedUrl = "https://release-assets.githubusercontent.com/signed-asset";

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = [];
        /// <summary>8-13 请求计数（退避断言用）</summary>
        public int Calls;
        /// <summary>8-13 所有请求的 Authorization 头（token 头断言用——redirect 模拟会重建请求丢头，看全部）</summary>
        public readonly List<string?> Auths = [];

        public void RouteStatus(string path, int status) =>
            _routes[path] = () => new HttpResponseMessage((HttpStatusCode)status);

        public void RouteJson(string path, string json) =>
            _routes[path] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };

        public void RouteRedirect(string path, string location) =>
            _routes[path] = () =>
            {
                var msg = new HttpResponseMessage(HttpStatusCode.Redirect);
                msg.Headers.Location = new Uri(location);
                return msg;
            };

        /// <summary>302 跟随后的最终目标（HttpClient 自动跟随会请求它）</summary>
        public void RouteOk(string path) =>
            _routes[path] = () => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Auths.Add(request.Headers.Authorization?.ToString());
            if (_routes.TryGetValue(request.RequestUri!.AbsolutePath, out var factory))
            {
                var resp = factory();
                // SocketsHttpHandler 会设置 RequestMessage 指向请求——fake 手动补（GitHubApiDirect 读它拿签名链）
                resp.RequestMessage = request;
                // 模拟 SocketsHttpHandler 的自动重定向（自定义 handler 不实现跟随——
                // 生产用 HttpClientPool 的 SocketsHttpHandler 默认跟随）
                if (resp.StatusCode == HttpStatusCode.Redirect && resp.Headers.Location is not null)
                    return SendAsync(new HttpRequestMessage(HttpMethod.Get, resp.Headers.Location), ct);
                return Task.FromResult(resp);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }

    private static void Use(FakeHandler handler)
    {
        GitHubApiDirect.ClearCacheForTest();
        GitHubApiDirect.TokenOverride = null;
        GitHubApiDirect.Http = new HttpClient(handler);
    }

    [Fact]
    public async Task ResolvesSignedUrl_FromTagAndAsset()
    {
        var handler = new FakeHandler();
        handler.RouteJson("/repos/yt-dlp/yt-dlp/releases/tags/2026.07.04",
            """{"assets":[{"id":466665334,"name":"yt-dlp.exe"},{"id":1,"name":"yt-dlp.tar.gz"}]}""");
        handler.RouteRedirect("/repos/yt-dlp/yt-dlp/releases/assets/466665334", SignedUrl);
        handler.RouteOk("/signed-asset");
        Use(handler);

        var url = await GitHubApiDirect.GetSignedUrlAsync("ghapi:yt-dlp/yt-dlp/2026.07.04/yt-dlp.exe", CancellationToken.None);

        Assert.Equal(SignedUrl, url);
    }

    [Fact]
    public async Task AssetNameMismatch_ReturnsNull()
    {
        var handler = new FakeHandler();
        handler.RouteJson("/repos/yt-dlp/yt-dlp/releases/tags/2026.07.04",
            """{"assets":[{"id":466665334,"name":"yt-dlp.tar.gz"}]}""");
        Use(handler);

        var url = await GitHubApiDirect.GetSignedUrlAsync("ghapi:yt-dlp/yt-dlp/2026.07.04/yt-dlp.exe", CancellationToken.None);

        Assert.Null(url);
    }

    [Fact]
    public async Task TagNotFound_ReturnsNull()
    {
        Use(new FakeHandler()); // 无路由 → 404

        var url = await GitHubApiDirect.GetSignedUrlAsync("ghapi:yt-dlp/yt-dlp/2026.07.04/yt-dlp.exe", CancellationToken.None);

        Assert.Null(url);
    }

    [Fact]
    public async Task NonGhapiUrl_ReturnsNull()
    {
        var url = await GitHubApiDirect.GetSignedUrlAsync("https://github.com/foo/bar/releases/download/v1/x.exe", CancellationToken.None);
        Assert.Null(url);
    }

    [Fact]
    public async Task MalformedGhapi_ReturnsNull()
    {
        var url = await GitHubApiDirect.GetSignedUrlAsync("ghapi:only-three", CancellationToken.None);
        Assert.Null(url);
    }

    [Fact]
    public async Task SignedUrl_Cached()
    {
        var handler = new FakeHandler();
        handler.RouteJson("/repos/yt-dlp/yt-dlp/releases/tags/2026.07.04",
            """{"assets":[{"id":466665334,"name":"yt-dlp.exe"}]}""");
        handler.RouteRedirect("/repos/yt-dlp/yt-dlp/releases/assets/466665334", SignedUrl);
        handler.RouteOk("/signed-asset");
        Use(handler);

        await GitHubApiDirect.GetSignedUrlAsync("ghapi:yt-dlp/yt-dlp/2026.07.04/yt-dlp.exe", CancellationToken.None);
        // 换链成功后改路由（模拟 API 不可用）——缓存命中应仍返回签名 URL
        handler.RouteJson("/repos/yt-dlp/yt-dlp/releases/tags/2026.07.04", """{"assets":[]}""");

        var url = await GitHubApiDirect.GetSignedUrlAsync("ghapi:yt-dlp/yt-dlp/2026.07.04/yt-dlp.exe", CancellationToken.None);
        Assert.Equal(SignedUrl, url);
    }

    [Fact]
    public async Task RateLimited_BacksOff_NoRepeatedApiCalls()
    {
        // 8-13 失败退避：403（限流）后 5 分钟不再打 API——否则每轮重试 Resolve 都再打，
        // 重试风暴耗尽未认证 60 次/小时额度（真机 8-13 候选 6→3 源即此）
        var handler = new FakeHandler();
        handler.RouteStatus("/repos/yt-dlp/yt-dlp/releases/tags/2026.07.04", 403);
        Use(handler);

        Assert.Null(await GitHubApiDirect.GetSignedUrlAsync(
            "ghapi:yt-dlp/yt-dlp/2026.07.04/yt-dlp.exe", CancellationToken.None));
        var callsAfterFirst = handler.Calls;

        // 限流期内第二次调用：直接放弃，不再打 API
        Assert.Null(await GitHubApiDirect.GetSignedUrlAsync(
            "ghapi:yt-dlp/yt-dlp/2026.07.04/yt-dlp.exe", CancellationToken.None));
        Assert.Equal(callsAfterFirst, handler.Calls);
    }

    [Fact]
    public async Task TokenConfigured_SendsAuthorizationHeader()
    {
        // 8-13 配置 token → 请求带 Bearer 头（限流 60→5000 次/小时）
        var handler = new FakeHandler();
        handler.RouteJson("/repos/yt-dlp/yt-dlp/releases/tags/2026.07.04",
            """{"assets":[{"id":466665334,"name":"yt-dlp.exe"}]}""");
        handler.RouteRedirect("/repos/yt-dlp/yt-dlp/releases/assets/466665334", SignedUrl);
        handler.RouteOk("/signed-asset");
        Use(handler);
        GitHubApiDirect.TokenOverride = "ghp_test123";

        var url = await GitHubApiDirect.GetSignedUrlAsync(
            "ghapi:yt-dlp/yt-dlp/2026.07.04/yt-dlp.exe", CancellationToken.None);

        Assert.Equal(SignedUrl, url);
        Assert.Contains("Bearer ghp_test123", handler.Auths); // tags/assets 两个 API 请求都带（redirect 模拟重建丢头，看全部）
    }

    [Fact]
    public async Task NoToken_NoAuthorizationHeader()
    {
        // 8-13 未配置 token（普通用户默认模式）→ 不带 Authorization 头
        var handler = new FakeHandler();
        handler.RouteJson("/repos/yt-dlp/yt-dlp/releases/tags/2026.07.04",
            """{"assets":[{"id":466665334,"name":"yt-dlp.exe"}]}""");
        handler.RouteRedirect("/repos/yt-dlp/yt-dlp/releases/assets/466665334", SignedUrl);
        handler.RouteOk("/signed-asset");
        Use(handler);
        GitHubApiDirect.TokenOverride = "";

        var url = await GitHubApiDirect.GetSignedUrlAsync(
            "ghapi:yt-dlp/yt-dlp/2026.07.04/yt-dlp.exe", CancellationToken.None);

        Assert.Equal(SignedUrl, url);
        Assert.DoesNotContain(handler.Auths, a => a is not null); // 无 token → 所有请求都不带 Authorization
    }
}
