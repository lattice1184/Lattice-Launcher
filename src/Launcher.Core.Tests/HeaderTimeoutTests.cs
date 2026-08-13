using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>响应头超时（AL64）：TCP 半开连接不卡死——响应头超时判源死换路（真机 08-11 卡 10 小时场景）</summary>
public class HeaderTimeoutTests
{
    private sealed class HangingHandler : HttpMessageHandler
    {
        private readonly string _hangHost;
        public HangingHandler(string hangHost) => _hangHost = hangHost;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == _hangHost)
            {
                await Task.Delay(TimeSpan.FromSeconds(120), ct); // 挂起——ct 取消才返回（半开连接模拟）
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("OKOK"u8.ToArray()) };
        }
    }

    private static DownloadService CreateService(HttpMessageHandler handler, params string[] urls)
    {
        var resolver = new FixedResolver(urls);
        return new DownloadService(new HttpClient(handler), resolver, new DownloadOptions
        {
            MaxSourceAttempts = 2,
            ResponseHeaderTimeoutMs = 300, // 注入短超时——测试不等 30s
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath(), (_, _) => Task.FromResult(true));
    }

    [Fact]
    public async Task HangingHeader_SingleCandidate_FailsFastNotHangs()
    {
        // 单候选挂起源：响应头 300ms 超时 → 转可重试错误 → 重试耗尽抛错——总耗时 <5s（不卡 10 小时）
        var handler = new HangingHandler("hang.com");
        var svc = CreateService(handler, "https://hang.com/f.bin");
        var dest = Path.Combine(Path.GetTempPath(), $"hang1-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                svc.DownloadFileAsync("https://hang.com/f.bin", dest, null, 4, _ => { }, CancellationToken.None));
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
                $"总耗时 {sw.Elapsed.TotalSeconds:F1}s——挂起源未判死，卡死复现");
        }
        finally
        {
            File.Delete(dest);
        }
    }

    [Fact]
    public async Task HangingHeader_FirstSourceDead_SecondWins()
    {
        // 双候选竞速：a 挂起（300ms 判死）+ b 正常 → b 赢，下载成功不卡死
        var handler = new HangingHandler("hang.com");
        var svc = CreateService(handler, "https://hang.com/f.bin", "https://good.com/f.bin");
        var dest = Path.Combine(Path.GetTempPath(), $"hang2-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = Stopwatch.StartNew();
            await svc.DownloadFileAsync("https://hang.com/f.bin", dest, null, 4, _ => { }, CancellationToken.None);
            sw.Stop();
            Assert.Equal("OKOK", await File.ReadAllTextAsync(dest));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
                $"总耗时 {sw.Elapsed.TotalSeconds:F1}s——挂起源拖住了竞速");
        }
        finally
        {
            File.Delete(dest);
        }
    }

    private sealed class FixedResolver : IDlSourceResolver
    {
        private readonly string[] _urls;
        public FixedResolver(params string[] urls) => _urls = urls;
        public IReadOnlyList<string> Resolve(string officialUrl) => _urls;
    }
}
