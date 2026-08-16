using System.Net;
using System.Net.Http;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>
/// 分片并发决策（AL60 ramp-up）+ 8-18 固定片大小：先探测单连接速度再定并发（限并发源自动降单连接，
/// 按连接限速源吃满并发）；片边界固定 256KB——探测只决定同时下几片，不再决定片边界（换源续进度核心）。
/// </summary>
public class RampUpTests
{
    private sealed class RangeHandler : HttpMessageHandler
    {
        public readonly List<string> Ranges = [];
        public TimeSpan Delay;
        private readonly object _lock = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            lock (_lock) Ranges.Add(range is null ? "full" : $"{range.From}-{range.To}");
            await Task.Delay(Delay, ct);
            long len = range is null || range.From is null || range.To is null
                ? 0 : range.To.Value - range.From.Value + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[len]) };
        }
    }

    private static DownloadService CreateService(HttpMessageHandler handler, int chunkCount = 8)
        => new(new HttpClient(handler), null, new DownloadOptions
        {
            MaxSourceAttempts = 1,
            ChunkCount = chunkCount,
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath(), (_, _) => Task.FromResult(true));

    [Theory]
    // 快源：探测 0.1s 拉完 1MB → ~10MB/s → 单连接（限并发源：分片只会触发限流）
    [InlineData(100, 1)]
    // 中速：1.5s 拉 1MB → ~667KB/s → 4 并发
    [InlineData(1500, 4)]
    // 慢源：2s 窗口截断（0 字节）→ 满并发 8（按连接限速源需要分片）
    [InlineData(5000, 8)]
    public async Task Probe_DecidesConcurrencyBySpeed(int delayMs, int expectedConcurrency)
    {
        var handler = new RangeHandler { Delay = TimeSpan.FromMilliseconds(delayMs) };
        var svc = CreateService(handler);
        var partDir = Path.Combine(Path.GetTempPath(), $"probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(partDir); // 生产路径由 DownloadChunkedAsync 创建；直接测探测函数需自建（探测写 probe.part）
        try
        {
            var concurrency = await svc.ProbeAndDecideConcurrencyAsync("https://example.com/f.bin", 3 * 1024 * 1024, partDir, CancellationToken.None,
                new DownloadService.ThrottleState()); // 8-22 探测也走共享节流（限速 0 时无副作用）
            Assert.Equal(expectedConcurrency, concurrency);
        }
        finally
        {
            try { Directory.Delete(partDir, true); } catch { }
        }
    }

    [Fact]
    public async Task FixedChunk_SizeDeterminesChunkCount_NotConcurrency()
    {
        // 8-18：3MB 文件固定片 1MB → 3 片（Range = 探测 1 + 正式 3）；并发只影响同时下几片
        var handler = new RangeHandler { Delay = TimeSpan.FromMilliseconds(100) };
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"ramp3-{Guid.NewGuid():N}.bin");
        const long size = 3 * 1024 * 1024;
        try
        {
            await svc.DownloadFileAsync("https://example.com/f.bin", dest, null, size, _ => { }, CancellationToken.None);
            Assert.True(1 + 3 == handler.Ranges.Count,
                $"期望 4 个 Range（探测 1 + 3 片），实际 {handler.Ranges.Count}: {string.Join(", ", handler.Ranges)}");
            Assert.Equal(size, new FileInfo(dest).Length);
        }
        finally
        {
            File.Delete(dest);
        }
    }

    [Theory]
    // 8-19 快源大文件保底并发：RTT 惩罚对吞吐不可见（升片永不触发）——探测时刻摊薄 4 并发
    [InlineData(100, 100L * 1024 * 1024, 4)]  // 快源 + 100MB → 保底 4
    [InlineData(100, 8L * 1024 * 1024, 1)]    // 快源 + 恰 8MB → 1（限并发源不受影响）
    [InlineData(100, 8L * 1024 * 1024 + 1, 4)] // 快源 + 8MB+1 → 保底 4
    public async Task Probe_FastSource_LargeFile_FloorsConcurrency(int delayMs, long totalSize, int expected)
    {
        var handler = new RangeHandler { Delay = TimeSpan.FromMilliseconds(delayMs) };
        var svc = CreateService(handler);
        var partDir = Path.Combine(Path.GetTempPath(), $"probe2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(partDir);
        try
        {
            var concurrency = await svc.ProbeAndDecideConcurrencyAsync("https://example.com/f.bin", totalSize, partDir, CancellationToken.None,
                new DownloadService.ThrottleState()); // 8-22 探测也走共享节流（限速 0 时无副作用）
            Assert.Equal(expected, concurrency);
        }
        finally
        {
            try { Directory.Delete(partDir, true); } catch { }
        }
    }

    [Fact]
    public async Task SmallFile_NoProbe_ChunksByFixedSize()
    {
        // < 1MB：不探测（探测段≈整个文件无意义），并发满额；片数 = ceil(size/1MB) = 1
        var handler = new RangeHandler { Delay = TimeSpan.FromMilliseconds(100) };
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"ramp2-{Guid.NewGuid():N}.bin");
        const long size = 500 * 1024;
        try
        {
            await svc.DownloadFileAsync("https://example.com/f.bin", dest, null, size, _ => { }, CancellationToken.None);
            Assert.Equal(1, handler.Ranges.Count);
            Assert.Equal(size, new FileInfo(dest).Length);
        }
        finally
        {
            File.Delete(dest);
        }
    }
}
