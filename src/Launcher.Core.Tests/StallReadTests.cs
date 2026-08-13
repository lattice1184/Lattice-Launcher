using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>
/// body 读心跳（AL66）：响应头到了、body 中途静默断流（TCP 半开）→ ReadAsync 挂起——
/// AL64 头超时管不到（头早到了）、AL61 慢速检测挂在数据循环体内跑不到、单候选无竞速换路
/// → 之前 fabric-api 卡 0.2MB 3 分钟+。心跳：每轮数据重置 N 秒定时器，挂起 → 判死换路。
/// </summary>
public class StallReadTests
{
    /// <summary>先吐 chunk 数据，然后 ReadAsync 挂起等待取消（模拟 body 中途静默断流）</summary>
    private sealed class StallStream : Stream
    {
        private readonly int _initialChunk;
        private readonly Action? _onStallCancelled;
        private bool _sent;

        public StallStream(int initialChunk, Action? onStallCancelled = null)
        {
            _initialChunk = initialChunk;
            _onStallCancelled = onStallCancelled;
        }

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
                var n = Math.Min(_initialChunk, count);
                Array.Fill(buffer, (byte)'X', offset, n);
                return n;
            }
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // 静默挂起：只有取消/心跳能解开
            }
            catch (OperationCanceledException)
            {
                _onStallCancelled?.Invoke();
                throw;
            }
            return 0;
        }
    }

    private sealed class StalledHandler : HttpMessageHandler
    {
        // 工厂模式：每请求新建内容——分片并发共享同一 StreamContent 实例会被先 dispose 的片打爆
        private readonly Dictionary<string, Func<HttpContent>> _routes = [];
        public readonly List<string> Cancelled = [];

        public void RouteStalled(string host, int initialChunk)
            => _routes[host] = () => new StreamContent(new StallStream(initialChunk, () => { lock (Cancelled) Cancelled.Add(host); }));
        public void RouteBytes(string host, byte[] body)
            => _routes[host] = () => new ByteArrayContent(body);
        public void RouteDelayed(string host, int delayMs, byte[] body)
            => _routes[host] = () => new DelayedContent(delayMs, body);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (_routes.TryGetValue(request.RequestUri!.Host, out var factory))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = factory() });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>响应头立即返回，body 延迟 delayMs 才给数据（模拟慢启动/排队——不挂死）</summary>
    private sealed class DelayedContent : HttpContent
    {
        private readonly int _delayMs;
        private readonly byte[] _body;

        public DelayedContent(int delayMs, byte[] body)
        {
            _delayMs = delayMs;
            _body = body;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => throw new NotSupportedException();

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(_delayMs, cancellationToken);
            await stream.WriteAsync(_body, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _body.Length;
            return true;
        }
    }

    private sealed class FixedResolver : IDlSourceResolver
    {
        private readonly string[] _urls;
        public FixedResolver(params string[] urls) => _urls = urls;
        public IReadOnlyList<string> Resolve(string officialUrl) => _urls;
    }

    private static DownloadService CreateService(HttpMessageHandler handler, IDlSourceResolver? resolver = null,
        int maxAttempts = 1, long stallMs = 500)
    {
        return new DownloadService(new HttpClient(handler), resolver, new DownloadOptions
        {
            MaxSourceAttempts = maxAttempts,
            ReadStallTimeoutMs = stallMs,
            SlowSpeedBps = 0, // 关慢速检测（速度不可能 < 0）——单测专注 stall 心跳
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath(), (_, _) => Task.FromResult(true));
    }

    [Fact]
    public async Task StalledBody_SingleCandidate_ThrowsWithoutHanging()
    {
        // 单候选（无竞速换路）：0.2MB 后静默 → 500ms 心跳判死 → 抛可重试错误，不永久卡死
        var handler = new StalledHandler();
        handler.RouteStalled("stall.com", 200 * 1024);
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"stall1-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                svc.DownloadFileAsync("https://stall.com/f.bin", dest, null, 500 * 1024, _ => { }, CancellationToken.None));
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
                $"卡死未解：{sw.Elapsed.TotalSeconds:F1}s（应 ~0.5s 心跳判死）");
        }
        finally
        {
            File.Delete(dest);
        }
    }

    [Fact]
    public async Task StalledBody_DualCandidate_FallsOverToLiveSource()
    {
        // 竞速双候选：stall.com 静默 + fast.com 延迟 1.5s 才给数据——
        // stall 心跳 500ms 先判死（记录 Cancelled），1.5s 后 fast 完成 → 快源赢
        var handler = new StalledHandler();
        handler.RouteStalled("stall.com", 200 * 1024);
        handler.RouteDelayed("fast.com", 1500, "LIVE"u8.ToArray());
        var svc = CreateService(handler, new FixedResolver("http://stall.com/f.bin", "http://fast.com/f.bin"));
        var dest = Path.Combine(Path.GetTempPath(), $"stall2-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = Stopwatch.StartNew();
            await svc.DownloadFileAsync("http://stall.com/f.bin", dest, null, 4, _ => { }, CancellationToken.None);
            sw.Stop();
            Assert.Equal("LIVE", await File.ReadAllTextAsync(dest));
            Assert.Contains("stall.com", handler.Cancelled); // 心跳真把 stall 判死了（不是 fast 先赢）
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"换路太慢：{sw.Elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            File.Delete(dest);
        }
    }

    [Fact]
    public async Task BriefPause_ThenDataResumes_NotKilled()
    {
        // 数据持续到达就重置心跳——短暂停顿（< 心跳窗口）后恢复，不误杀
        // 200KB < ChunkThreshold(256KB) → 单连接路径（分片 8 并发流有 fake handler 竞态，真机无此问题）
        var handler = new InterruptibleHandler(initial: 150 * 1024, pauseMs: 200, resume: 50 * 1024);
        var svc = CreateService(handler, stallMs: 1000);
        var dest = Path.Combine(Path.GetTempPath(), $"stall3-{Guid.NewGuid():N}.bin");
        try
        {
            await svc.DownloadFileAsync("https://blip.com/f.bin", dest, null, 200 * 1024, _ => { }, CancellationToken.None);
            Assert.Equal(200 * 1024, new FileInfo(dest).Length);
        }
        finally
        {
            File.Delete(dest);
        }
    }

    /// <summary>initial 字节 → 停顿 pauseMs（不挂死，主动恢复）→ resume 字节 → EOF；支持 Range 区间</summary>
    private sealed class InterruptibleHandler : HttpMessageHandler
    {
        private readonly long _initial;
        private readonly int _pauseMs;
        private readonly long _resume;

        public InterruptibleHandler(long initial, int pauseMs, long resume)
        {
            _initial = initial;
            _pauseMs = pauseMs;
            _resume = resume;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            long total = _initial + _resume;
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            var start = range?.From ?? 0;
            var end = range?.To ?? total - 1;
            var stream = new ChunkedWithPause(_initial, _pauseMs, _resume, start, end - start + 1);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        }
    }

    private sealed class ChunkedWithPause : Stream
    {
        private readonly long _initial;
        private readonly int _pauseMs;
        private readonly long _resume;
        private readonly long _rangeStart;
        private readonly long _expected;
        private long _sent;

        public ChunkedWithPause(long initial, int pauseMs, long resume, long rangeStart, long expected)
        {
            _initial = initial;
            _pauseMs = pauseMs;
            _resume = resume;
            _rangeStart = rangeStart;
            _expected = expected;
        }

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
            if (_sent >= _expected) return 0; // 自己的 Range 段读满 → EOF（真实服务器语义）
            var abs = _rangeStart + _sent;
            if (abs == _initial) await Task.Delay(_pauseMs, cancellationToken); // 主动停顿（非挂死）
            var remaining = abs < _initial ? _initial - abs : _resume;
            var n = (int)Math.Min(16 * 1024, Math.Min(remaining, _expected - _sent));
            Array.Fill(buffer, (byte)'Y', offset, n);
            _sent += n;
            return n;
        }
    }
}
