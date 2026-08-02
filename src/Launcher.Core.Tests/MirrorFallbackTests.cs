using System.Net;
using System.Net.Http;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>镜像回退：官方失败→镜像成功 / 官方坏字节→镜像好字节 / 双失败按次数 / 不可映射 URL 单候选</summary>
public class MirrorFallbackTests
{
    /// <summary>按 host+path 返回状态/内容；跟踪请求序列</summary>
    private sealed class HostStubHandler : HttpMessageHandler
    {
        public readonly List<string> Requests = [];
        private readonly Dictionary<string, (int Status, byte[] Body)> _routes = [];
        private readonly byte[] _defaultBody = "12345"u8.ToArray();

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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_defaultBody) });
        }
    }

    private static DownloadService CreateService(HostStubHandler handler, DownloadOptions? options = null)
    {
        var http = new HttpClient(handler);
        // 官方源：any host；镜像源：bmclapi2.bangbang93.com
        var resolver = new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper());
        return new DownloadService(http, resolver, options ?? new DownloadOptions
        {
            MaxSourceAttempts = 2,
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath());
    }

    [Fact]
    public async Task OfficialFails_MirrorSucceeds()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("resources.download.minecraft.net/ab/abcdef", 500, []);
        handler.RouteBytes("bmclapi2.bangbang93.com/ab/abcdef", 200, "12345"u8.ToArray());
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"mirror-{Guid.NewGuid():N}.jar");
        try
        {
            var url = "https://resources.download.minecraft.net/ab/abcdef";
            await svc.DownloadFileAsync(url, dest, null, 5, null, CancellationToken.None);

            Assert.True(File.Exists(dest));
            Assert.Equal(5, new FileInfo(dest).Length);
            Assert.Contains(handler.Requests, r => r.Contains("bmclapi2.bangbang93.com")); // 镜像被请求
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task OfficialWrongBytes_MirrorCorrectBytes_Wins()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("libraries.minecraft.net/org/a/1.0/a-1.0.jar", 200, "WRONG!!"u8.ToArray());
        handler.RouteBytes("bmclapi2.bangbang93.com/maven/org/a/1.0/a-1.0.jar", 200, "12345"u8.ToArray());
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"mirror-{Guid.NewGuid():N}.jar");
        try
        {
            // 官方 URL 无法映射镜像（libraries.minecraft.net 可映射到 /maven）→ 校验失败后换镜像
            var url = "https://libraries.minecraft.net/org/a/1.0/a-1.0.jar";
            await svc.DownloadFileAsync(url, dest, null, 5, null, CancellationToken.None);

            Assert.True(File.Exists(dest));
            Assert.Equal(5, new FileInfo(dest).Length); // 镜像的好字节胜出
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task BothSourcesFail_ThrowsAfterAttempts()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("resources.download.minecraft.net/ab/abcdef", 500, []);
        handler.RouteBytes("bmclapi2.bangbang93.com/ab/abcdef", 500, []);
        var svc = CreateService(handler); // MaxSourceAttempts=2 → 每轮 2 源 → 4 次请求
        var dest = Path.Combine(Path.GetTempPath(), $"mirror-{Guid.NewGuid():N}.jar");
        try
        {
            var url = "https://resources.download.minecraft.net/ab/abcdef";
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                svc.DownloadFileAsync(url, dest, null, 5, null, CancellationToken.None));

            Assert.Equal(4, handler.Requests.Count); // 2 轮 × 2 源
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task UnmappableUrl_SingleCandidateNoDuplicates()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("custom.example.com/x.jar", 200, "12345"u8.ToArray());
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"mirror-{Guid.NewGuid():N}.jar");
        try
        {
            var url = "https://custom.example.com/x.jar";
            await svc.DownloadFileAsync(url, dest, null, 5, null, CancellationToken.None);

            // 不可映射 → 每轮只有官方一个候选；成功即止 → 只请求一次
            Assert.Single(handler.Requests);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task NetworkUnreachable_AfterRetries_ReportsClearly()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("resources.download.minecraft.net/ab/abcdef", 500, []);
        handler.RouteBytes("bmclapi2.bangbang93.com/ab/abcdef", 500, []);
        var http = new HttpClient(handler);
        var resolver = new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper());
        // 注入网络检查：报告不可达
        var svc = new DownloadService(http, resolver, new DownloadOptions
        {
            MaxSourceAttempts = 2,
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath(), (hosts, ct) => Task.FromResult(false));
        var dest = Path.Combine(Path.GetTempPath(), $"mirror-{Guid.NewGuid():N}.jar");
        try
        {
            var url = "https://resources.download.minecraft.net/ab/abcdef";
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.DownloadFileAsync(url, dest, null, 5, null, CancellationToken.None));

            Assert.Contains("网络不可达", ex.Message);
            Assert.Contains("resources.download.minecraft.net", ex.Message);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task MirrorFallbackDisabled_OnlyOfficialCandidate()
    {
        var handler = new HostStubHandler();
        handler.RouteBytes("resources.download.minecraft.net/ab/abcdef", 200, "12345"u8.ToArray());
        var http = new HttpClient(handler);
        var resolver = new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper());
        var svc = new DownloadService(http, resolver, new DownloadOptions
        {
            MirrorFallbackEnabled = false,
            MaxSourceAttempts = 2,
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath());
        var dest = Path.Combine(Path.GetTempPath(), $"mirror-{Guid.NewGuid():N}.jar");
        try
        {
            var url = "https://resources.download.minecraft.net/ab/abcdef";
            await svc.DownloadFileAsync(url, dest, null, 5, null, CancellationToken.None);

            // 镜像禁用 → 只请求官方
            Assert.All(handler.Requests, r => Assert.DoesNotContain("bmclapi2", r));
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }
}
