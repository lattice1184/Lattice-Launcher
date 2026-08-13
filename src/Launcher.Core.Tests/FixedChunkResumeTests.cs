using System.Net;
using System.Net.Http;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>
/// 8-18 固定分片续传（换源续进度核心）：片边界固定 256KB → 已完成片跨调用/换源/并发变化复用，
/// 失败重试只补下剩余片，不再从零重下。
/// </summary>
public class FixedChunkResumeTests
{
    private const long MB = 1024 * 1024;

    /// <summary>先吐 N 字节然后挂起等待取消（中断残留模拟）</summary>
    private sealed class PartialThenStallStream : Stream
    {
        private readonly int _initial;
        private bool _sent;

        public PartialThenStallStream(int initial) => _initial = initial;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_sent)
            {
                _sent = true;
                var n = Math.Min(_initial, count);
                Array.Fill(buffer, (byte)'P', offset, n);
                return n;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    /// <summary>按 Range start 记录请求；指定片（start=StallFrom）首次请求吐 16KB 后挂起，后续正常</summary>
    private sealed class StallChunkHandler : HttpMessageHandler
    {
        private readonly long _total;
        public readonly List<(long Start, long End)> Requests = [];
        private readonly object _lock = new();
        private bool _stalledOnce;
        public long StallFrom = -1;

        public StallChunkHandler(long total) => _total = total;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is null)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[_total]) };
            var start = range.From!.Value;
            var end = range.To!.Value;
            var len = end - start + 1;
            lock (_lock)
            {
                Requests.Add((start, end));
                // 一次性开关（不受 Requests.Clear 影响）：StallFrom 片只挂起一次，之后正常——第二次调用续传
                if (start == StallFrom && !_stalledOnce)
                {
                    _stalledOnce = true;
                    return new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new StreamContent(new PartialThenStallStream(16 * 1024)),
                    };
                }
            }
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(new byte[len]),
            };
        }
    }

    private static DownloadService CreateService(HttpMessageHandler handler, bool stallAsSlow = false)
        => new(new HttpClient(handler), null, new DownloadOptions
        {
            MaxSourceAttempts = stallAsSlow ? 2 : 1, // 慢速档：attempt 内换源续传（attempt 0 判死 → attempt 1 复用）
            ReadStallTimeoutMs = 300,
            SlowSpeedBps = stallAsSlow ? 1024 * 1024 : 0, // 慢速判死档：片挂起 → 100ms 判死 SlowSourceException
            SlowProbeMs = stallAsSlow ? 50 : 5000,
            SlowSamples = stallAsSlow ? 2 : 6,
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath(), (_, _) => Task.FromResult(true));

    [Fact]
    public async Task RetryAttempt_ReusesCompletedChunks()
    {
        // 3.5MB 固定片 1MB = 4 片（start: 0/1M/2M/3M）。attempt 0：快源探测 → 单连接，
        // 片 0→1 完成，片 2（start=2MB）挂起（吐 16KB 后停）→ 慢速判死 → SlowSourceException 换路；
        // attempt 1（同 URL 重新 Resolve）：已完成片 0-1 复用（不请求），只补片 2（16KB 断点续传）和片 3。
        // 换源续进度核心断言：片 1 全程只请求 1 次（attempt 0），attempt 1 复用未重下。
        const long total = MB * 3 + MB / 2;
        var handler = new StallChunkHandler(total) { StallFrom = 2 * MB };
        var svc = CreateService(handler, stallAsSlow: true);
        var dest = Path.Combine(Path.GetTempPath(), $"fx1-{Guid.NewGuid():N}.bin");
        try
        {
            await svc.DownloadFileAsync("https://example.com/f.bin", dest, null, total, _ => { }, CancellationToken.None);

            Assert.Equal(total, new FileInfo(dest).Length);
            var starts = handler.Requests.Select(r => r.Start).ToList();
            Assert.Equal(1, starts.Count(s => s == MB));                // 片 1 仅 attempt 0 请求（复用）
            Assert.Contains(2 * MB + 16 * 1024, starts);                // attempt 1 片 2：从 16KB 断点续传
            Assert.Equal(1, starts.Count(s => s == 3 * MB));            // 片 3 仅 attempt 1 下
            Assert.Equal(3, starts.Count(s => s == 0));                 // 探测×2 + 片 0 首次
        }
        finally
        {
            File.Delete(dest);
            try { Directory.Delete(dest + ".parts", true); } catch { }
        }
    }

    [Fact]
    public async Task ChunkBoundaries_Fixed_1MB()
    {
        // 边界恒 1MB 对齐（0/1M/2M/3M...）——升片/换源后边界不变是复用的前提
        const long total = 3 * MB;
        var handler = new StallChunkHandler(total);
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"fx2-{Guid.NewGuid():N}.bin");
        try
        {
            await svc.DownloadFileAsync("https://example.com/f.bin", dest, null, total, _ => { }, CancellationToken.None);
            Assert.Equal(total, new FileInfo(dest).Length);
            var starts = handler.Requests.Select(r => r.Start).Where(s => s != 0).Distinct().OrderBy(s => s).ToList();
            // 3 片边界 1MB 对齐（片 0 与探测同 start=0 无法区分，故去 0 后为 2）
            Assert.Equal(2, starts.Count);
            Assert.All(starts, s => Assert.Equal(0, s % MB));
        }
        finally
        {
            File.Delete(dest);
            try { Directory.Delete(dest + ".parts", true); } catch { }
        }
    }
}
