using System.Net;
using System.Net.Http;
using Launcher.Core.Download;
using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>
/// 8-22 共享节流器（根治「限速下载慢 + 一瞬间速度超额」）：总吞吐恒=设定值，与并发流数/源数无关。
/// - 单流：限速 256KB/s 下 512KB 期望 ≈2s——旧实现按满片均分（ChunkCount=8）1 片只有 32KB/s → 16s
///   （Fabric API 等小文件「浪费太多时间」的直接来源）
/// - 多源竞速：双源并行限速 512KB/s 总速率仍=设定值——旧实现每源各吃满片配额 → 2× 超额
///   （用户观察到的「一瞬间速度超额」）
/// - 探测段：限速下探测段同样节流——旧实现探测 1MB 全速拉爆（限速形同虚设）
/// </summary>
public class ThrottleTests
{
    /// <summary>全速吐字节桩（Range 感知；零延迟远小于慢速判死 30s 窗口——不会误判死）</summary>
    private sealed class FullSpeedHandler(long total) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            long len = total; // 无 Range 请求返回全长（200 全量响应语义）
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is { From: not null, To: not null })
                len = Math.Clamp(range.To.Value - range.From.Value + 1, 0, total);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[len]) });
        }
    }

    private static DownloadService CreateService(HttpMessageHandler handler, int bytesPerSecond, bool multiSource = false)
    {
        DownloadOptions opts = new()
        {
            MaxSourceAttempts = 1,
            ChunkCount = 8,
            BytesPerSecond = bytesPerSecond,
            BackoffProvider = _ => TimeSpan.Zero,
        };
        // 竞速测试：官方源 + bmclapi 镜像双候选（stub 全路由，host 无所谓）
        var resolver = multiSource
            ? new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper())
            : null;
        return new DownloadService(new HttpClient(handler), resolver, opts, Path.GetTempPath(),
            (_, _) => Task.FromResult(true));
    }

    [Fact]
    public async Task SingleStream_ReachesFullLimit()
    {
        // 512KB @ 256KB/s ≈ 2s；旧实现 1 片 = 256/8 = 32KB/s → 16s（必挂）
        var svc = CreateService(new FullSpeedHandler(512 * 1024), 256 * 1024);
        var dest = Path.Combine(Path.GetTempPath(), $"thr1-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await svc.DownloadFileAsync("https://example.com/f.bin", dest, null, 512 * 1024, null, CancellationToken.None);
            var secs = sw.Elapsed.TotalSeconds;
            Assert.True(secs >= 1.5, $"单流应吃满限速（≈2s），实际 {secs:0.0}s——疑似仍按满片均分");
            Assert.True(secs <= 5, $"单流不应明显超额，实际 {secs:0.0}s");
        }
        finally { try { File.Delete(dest); } catch { } }
    }

    [Fact]
    public async Task Race_MultipleSources_TotalStaysAtLimit()
    {
        // 2MB @ 512KB/s（双源竞速）期望 ≈3s；旧实现双源各吃满配额 → 1024KB/s → 1.5s（必挂）
        var svc = CreateService(new FullSpeedHandler(2 * 1024 * 1024), 512 * 1024, multiSource: true);
        var dest = Path.Combine(Path.GetTempPath(), $"thr2-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await svc.DownloadFileAsync("https://resources.download.minecraft.net/f.bin", dest, null, 2 * 1024 * 1024, null, CancellationToken.None);
            var secs = sw.Elapsed.TotalSeconds;
            Assert.True(secs >= 2.5, $"双源竞速总吞吐应=限速（≈3s），实际 {secs:0.0}s——疑似源数×配额超额");
            Assert.True(secs <= 6, $"双源竞速不应太慢，实际 {secs:0.0}s");
        }
        finally { try { File.Delete(dest); } catch { } }
    }

    [Fact]
    public async Task ProbeSegment_IsAlsoThrottled()
    {
        // 1.5MB @ 256KB/s：探测 1MB 受限 4s + 剩 0.5MB 受限 2s ≈ 6s；
        // 旧实现探测段全速（0.1s 拉爆 1MB）+ 分片限速 → ≈2s（必挂——「一瞬间超额」来源）
        var svc = CreateService(new FullSpeedHandler(1536 * 1024), 256 * 1024);
        var dest = Path.Combine(Path.GetTempPath(), $"thr3-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await svc.DownloadFileAsync("https://example.com/f.bin", dest, null, 1536 * 1024, null, CancellationToken.None);
            var secs = sw.Elapsed.TotalSeconds;
            Assert.True(secs >= 5, $"探测段应共享节流（≈6s），实际 {secs:0.0}s——疑似探测段全速拉爆");
            Assert.True(secs <= 10, $"探测段节流后不应更慢，实际 {secs:0.0}s");
        }
        finally { try { File.Delete(dest); } catch { } }
    }
}
