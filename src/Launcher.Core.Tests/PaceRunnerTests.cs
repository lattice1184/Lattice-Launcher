using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Launcher.Core.Download;
using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>
/// 陪跑（批次 41）：赢家降速（连续不增 + EMA &lt; 窗口次高值 50%）→ 后台重赛 2 个落选源 → 反超稳定顶替。
/// 桩：ScriptedSpeedHandler 按 (bytesPerSec, 秒数) 段表吐字节（Range 感知，206 + 断点续传）。
/// 关键时序约定：
/// 1. 落选源段表前 1-2 秒必须慢于赢家开局（否则 100ms 评估点 eta 外推会选落选源当赢家）；
/// 2. 采样 = 读循环逐读推拍（PaceTracker 按推拍差算逐读精确速率），250ms 上报节流不参与——
///    段表内平台速率精确恒定，降速链确定可触发；
/// 3. 单连接档（&lt;256KB）用 8KB 缓冲——220KB 文件 27 次读，降速段有足够推拍样本。
/// </summary>
public class PaceRunnerTests
{
    private const int TotalBytes = 220 * 1024; // 单连接档（<256KB ChunkThreshold）

    private static DownloadOptions PaceOpts(int evalMs = 100) => new()
    {
        DownloadSource = DownloadSourcePreference.OfficialFirst, // 显式官方顺序：本测试测竞速/陪跑时序，顺序非测试对象
        MaxSourceAttempts = 3, // 卡死测试需要第 3 轮（pace2 单独完成）
        BufferSize = 8192,     // 读粒度：220KB 文件 27 次读，逐读推拍采样充足
        RaceEliminateInterval = TimeSpan.FromMilliseconds(evalMs),
        PaceProbeIntervalMs = 100,
        PaceDeclineSamples = 5,
        PacePeakWindowSamples = 30,
        PaceDeclineRatio = 0.5,
        PaceStableLeadSamples = 3,
        PaceMinTotalBytes = 200 * 1024,
        PaceMinRemainBytes = 0,
        PaceCooldownMs = 0,
        PaceEliminateGraceMs = 10000, // 宽限须盖过陪跑源探针（分片档 2s）+ 慢起步段
        RaceWatchdogStallMs = 1000,
        BackoffProvider = _ => TimeSpan.Zero,
    };

    /// <summary>五段阶梯降速 + 长尾：峰值 50KB/s → 10KB/s → 慢尾 60s（EMA 在 20KB/s 段穿 25KB/s 触发线）</summary>
    private static (int Bps, double Seconds)[] DeclineScript() =>
    [
        (50 * 1024, 1), (40 * 1024, 1), (30 * 1024, 1), (20 * 1024, 1), (10 * 1024, 1), (10 * 1024, 60),
    ];

    /// <summary>落选源通用段表：开局慢（保住赢家在首次评估的领先）→ 之后提速到 targetBps</summary>
    private static (int Bps, double Seconds)[] PaceScript(int targetBps) => [(2 * 1024, 2), (targetBps, 60)];

    // ---------- 纯函数边界 ----------

    [Fact]
    public void ShouldTriggerPace_Boundary()
    {
        var o = new DownloadOptions
        {
            PaceDeclineSamples = 5, PaceDeclineRatio = 0.5,
            PaceMinTotalBytes = 100, PaceMinRemainBytes = 10,
        };
        Assert.True(DownloadService.PaceTracker.ShouldTriggerPace(o, 40, 100, 5, 1000, 500));  // 齐条件触发
        Assert.False(DownloadService.PaceTracker.ShouldTriggerPace(o, 51, 100, 5, 1000, 500)); // 速度未低于峰值 50%
        Assert.False(DownloadService.PaceTracker.ShouldTriggerPace(o, 40, 100, 4, 1000, 500)); // 下降样本不足
        Assert.False(DownloadService.PaceTracker.ShouldTriggerPace(o, 40, 100, 5, 99, 500));   // 文件小于门槛
        Assert.False(DownloadService.PaceTracker.ShouldTriggerPace(o, 40, 100, 5, 1000, 9));   // 剩余不足
        Assert.False(DownloadService.PaceTracker.ShouldTriggerPace(o, 40, 0, 5, 1000, 500));   // 无峰值（零速度源）
    }

    [Fact]
    public void ShouldTakeover_Boundary()
    {
        var o = new DownloadOptions { PaceStableLeadSamples = 3 };
        Assert.True(DownloadService.PaceTracker.ShouldTakeover(o, 100, 90, 3, 1000));   // 反超 + 稳定 3 拍
        Assert.False(DownloadService.PaceTracker.ShouldTakeover(o, 100, 90, 2, 1000));  // 稳定拍数不足
        Assert.False(DownloadService.PaceTracker.ShouldTakeover(o, 90, 100, 5, 1000));  // 未反超
        Assert.False(DownloadService.PaceTracker.ShouldTakeover(o, 100, 90, 5, 90));    // 旧赢家已收尾（bytes==total）不顶替
    }

    [Fact]
    public void PeakSpeed_SecondHighest_IgnoresSingleSpike()
    {
        // 开局瞬时突发 10MB/s 单拍，之后稳定 5MB/s：次高值 = 5MB/s——突发不顶高阈值线
        // （正常回落就不会误触「<峰值×50%」；旧取最高值会给出 5MB/s 的触发线）
        var t = new DownloadService.PaceTracker(new DownloadOptions { PacePeakWindowSamples = 30 });
        const long mb = 1024 * 1024;
        t.Sample(10 * mb, 1000);           // 基线
        t.Sample(15 * mb, 2000);           // 10MB/s 突发（唯一一拍）
        t.Sample(20 * mb, 3000);           // 5MB/s
        t.Sample(25 * mb, 4000);           // 5MB/s
        AssertPeakBps(t, 5 * mb);
    }

    [Fact]
    public void PeakSpeed_TwoSampleBurst_CountsAsPeak()
    {
        // 突发持续两拍 = 源的真实能力：次高值 = 10MB/s（真降速仍以它为准绳）
        var t = new DownloadService.PaceTracker(new DownloadOptions { PacePeakWindowSamples = 30 });
        const long mb = 1024 * 1024;
        t.Sample(10 * mb, 1000);           // 基线
        t.Sample(20 * mb, 2000);           // 10MB/s
        t.Sample(30 * mb, 3000);           // 10MB/s（第二拍——次高值升到 10）
        t.Sample(35 * mb, 4000);           // 5MB/s
        t.Sample(40 * mb, 5000);           // 5MB/s
        AssertPeakBps(t, 10 * mb);
    }

    private static void AssertPeakBps(DownloadService.PaceTracker t, long expectedBps)
        => Assert.InRange(t.PeakSpeed * 1000, expectedBps - 1024, expectedBps + 1024);

    // ---------- 集成 ----------

    [Fact]
    public async Task Trigger_DegradingWinner_AddsPaceSources()
    {
        var handler = new ScriptedSpeedHandler(TotalBytes,
            ("win.com", DeclineScript()),
            ("pace1.com", PaceScript(15 * 1024)),   // 触发后仍慢——旧赢家自己下完
            ("pace2.com", PaceScript(12 * 1024)));
        var http = new HttpClient(handler);
        var resolver = new FixedResolver(["http://win.com/f.bin", "http://pace1.com/f.bin", "http://pace2.com/f.bin"]);
        var svc = new DownloadService(http, resolver, PaceOpts(), Path.GetTempPath(), (_, _) => Task.FromResult(true));
        var dest = Path.Combine(Path.GetTempPath(), $"pace-t-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = Stopwatch.StartNew();
            await svc.DownloadFileAsync("http://win.com/f.bin", dest, null, TotalBytes, _ => { }, CancellationToken.None);
            sw.Stop();

            Assert.Equal(TotalBytes, new FileInfo(dest).Length);            // 下载完成
            Assert.True(handler.RequestCount("pace1.com") >= 2, "触发后 pace1 应有第二次请求（重赛入局）");
            Assert.True(handler.RequestCount("pace2.com") >= 2, "触发后 pace2 应有第二次请求（重赛入局）");
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"耗时 {sw.Elapsed.TotalSeconds:F1}s");
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task Takeover_OvertakerStableLead_DethronesWinner()
    {
        // 255KB（<256KB 仍单连接）：反超发生在 ~173KB 处，顶替后陪跑源还有 ~80KB 余量——
        // 足够 3 个稳定领先节拍（300ms × 150KB/s = 45KB），保证顶替先于完成发生
        const int total = 255 * 1024;
        var handler = new ScriptedSpeedHandler(total,
            ("win.com", DeclineScript()),
            ("pace1.com", [(2 * 1024, 1), (150 * 1024, 60)]),
            ("pace2.com", [(2 * 1024, 2), (20 * 1024, 60)]));
        var http = new HttpClient(handler);
        var resolver = new FixedResolver(["http://win.com/f.bin", "http://pace1.com/f.bin", "http://pace2.com/f.bin"]);
        var svc = new DownloadService(http, resolver, PaceOpts(), Path.GetTempPath(), (_, _) => Task.FromResult(true));
        var dest = Path.Combine(Path.GetTempPath(), $"pace-tk-{Guid.NewGuid():N}.bin");
        var progressSeq = new List<long>();
        try
        {
            var sw = Stopwatch.StartNew();
            await svc.DownloadFileAsync("http://win.com/f.bin", dest, null, total,
                p => { lock (progressSeq) progressSeq.Add(p.FileBytesDone); }, CancellationToken.None);
            sw.Stop();

            Assert.Equal(total, new FileInfo(dest).Length);
            Assert.Contains("win.com", handler.Cancelled);                  // 旧赢家被取消（顶替/赢家路径都必经）
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"耗时 {sw.Elapsed.TotalSeconds:F1}s——顶替未生效？");
            // UI 进度单调不减（RaceProgress Max 转发语义在顶替前后不回退）
            for (var i = 1; i < progressSeq.Count; i++)
                Assert.True(progressSeq[i] >= progressSeq[i - 1], $"进度回退：{progressSeq[i - 1]} → {progressSeq[i]}");
            // 成功路径清扫：无 .race 残留（stragglers 后台清理，轮询等它）
            await WaitRaceCleanAsync(dest);
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task NoTrigger_StableSpeed()
    {
        var handler = new ScriptedSpeedHandler(TotalBytes,
            ("win.com", [(100 * 1024, 30)]),
            ("pace1.com", PaceScript(120 * 1024)),
            ("pace2.com", PaceScript(80 * 1024)));
        var http = new HttpClient(handler);
        var resolver = new FixedResolver(["http://win.com/f.bin", "http://pace1.com/f.bin", "http://pace2.com/f.bin"]);
        var svc = new DownloadService(http, resolver, PaceOpts(), Path.GetTempPath(), (_, _) => Task.FromResult(true));
        var dest = Path.Combine(Path.GetTempPath(), $"pace-ns-{Guid.NewGuid():N}.bin");
        try
        {
            await svc.DownloadFileAsync("http://win.com/f.bin", dest, null, TotalBytes, _ => { }, CancellationToken.None);

            Assert.Equal(TotalBytes, new FileInfo(dest).Length);
            Assert.Equal(1, handler.RequestCount("pace1.com")); // 恒速无触发——落选源只被请求过一次
            Assert.Equal(1, handler.RequestCount("pace2.com"));
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task NoTrigger_SmallFile()
    {
        const int small = 100 * 1024; // < PaceMinTotalBytes(200KB)
        var handler = new ScriptedSpeedHandler(small,
            ("win.com", DeclineScript()),
            ("pace1.com", PaceScript(120 * 1024)),
            ("pace2.com", PaceScript(100 * 1024)));
        var http = new HttpClient(handler);
        var resolver = new FixedResolver(["http://win.com/f.bin", "http://pace1.com/f.bin", "http://pace2.com/f.bin"]);
        var svc = new DownloadService(http, resolver, PaceOpts(), Path.GetTempPath(), (_, _) => Task.FromResult(true));
        var dest = Path.Combine(Path.GetTempPath(), $"pace-sm-{Guid.NewGuid():N}.bin");
        try
        {
            await svc.DownloadFileAsync("http://win.com/f.bin", dest, null, small, _ => { }, CancellationToken.None);

            Assert.Equal(small, new FileInfo(dest).Length);
            Assert.Equal(1, handler.RequestCount("pace1.com")); // 小文件不陪跑
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task OldWinnerFinishesFirst_PaceSourcesCleanedAsStragglers()
    {
        // 触发陪跑后旧赢家突然提速并先下完——陪跑源作为 stragglers 取消清扫
        var handler = new ScriptedSpeedHandler(TotalBytes,
            ("win.com",
            [
                (50 * 1024, 1), (40 * 1024, 1), (30 * 1024, 1), (20 * 1024, 1), (10 * 1024, 1), (200 * 1024, 30),
            ]),
            ("pace1.com", PaceScript(20 * 1024)),
            ("pace2.com", PaceScript(15 * 1024)));
        var http = new HttpClient(handler);
        var resolver = new FixedResolver(["http://win.com/f.bin", "http://pace1.com/f.bin", "http://pace2.com/f.bin"]);
        var svc = new DownloadService(http, resolver, PaceOpts(), Path.GetTempPath(), (_, _) => Task.FromResult(true));
        var dest = Path.Combine(Path.GetTempPath(), $"pace-ow-{Guid.NewGuid():N}.bin");
        try
        {
            await svc.DownloadFileAsync("http://win.com/f.bin", dest, null, TotalBytes, _ => { }, CancellationToken.None);

            Assert.Equal(TotalBytes, new FileInfo(dest).Length);
            Assert.True(handler.RequestCount("pace1.com") >= 2, "应已触发陪跑（第二次请求）");
            await WaitRaceCleanAsync(dest); // stragglers 清理 + sweep 无残留
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task WinnerStalls_WatchdogAbandons_Pace2WinsNextRound()
    {
        // 赢家断流（降速样本不足不触发陪跑）→ count==1 watchdog 摘除 → 下一轮 pace1 也卡死被摘除
        // → 第 3 轮（MaxSourceAttempts=3）pace2 健康完成（不挂死）
        var handler = new ScriptedSpeedHandler(TotalBytes,
            ("win.com", [(50 * 1024, 1), (40 * 1024, 1), (0, 120)]),
            ("pace1.com", [(1 * 1024, 2), (30 * 1024, 2), (0, 120)]),
            ("pace2.com", [(2 * 1024, 2), (100 * 1024, 60)]));
        var http = new HttpClient(handler);
        var resolver = new FixedResolver(["http://win.com/f.bin", "http://pace1.com/f.bin", "http://pace2.com/f.bin"]);
        var svc = new DownloadService(http, resolver, PaceOpts(), Path.GetTempPath(), (_, _) => Task.FromResult(true));
        var dest = Path.Combine(Path.GetTempPath(), $"pace-wd-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = Stopwatch.StartNew();
            await svc.DownloadFileAsync("http://win.com/f.bin", dest, null, TotalBytes, _ => { }, CancellationToken.None);
            sw.Stop();

            Assert.Equal(TotalBytes, new FileInfo(dest).Length);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"耗时 {sw.Elapsed.TotalSeconds:F1}s——watchdog 未摘除卡死源");
            Assert.Equal(1, handler.RequestCount("win.com")); // 摘除进 abandonedKeys 后不再被请求
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task WinnerStalls_PaceTriggers_BeforeWatchdog()
    {
        // 8-15 断流触发陪跑：赢家快 1 秒后断流（零速度）——此前空拍跳过导致断流永不陪跑，
        // 只等 watchdog 30s 摘除（真机 13:04 Nexus-Player 卡死现场）。修复后断流 5 拍
        // （测试 500ms）即触发陪跑，先于 watchdog（1000ms）。
        var handler = new ScriptedSpeedHandler(TotalBytes,
            ("win.com", [(100 * 1024, 1), (0, 60)]),
            ("pace1.com", [(2 * 1024, 1), (150 * 1024, 60)]),
            ("pace2.com", [(2 * 1024, 2), (120 * 1024, 60)]));
        var http = new HttpClient(handler);
        var resolver = new FixedResolver(["http://win.com/f.bin", "http://pace1.com/f.bin", "http://pace2.com/f.bin"]);
        var svc = new DownloadService(http, resolver, PaceOpts(), Path.GetTempPath(), (_, _) => Task.FromResult(true));
        var dest = Path.Combine(Path.GetTempPath(), $"pace-stall-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = Stopwatch.StartNew();
            await svc.DownloadFileAsync("http://win.com/f.bin", dest, null, TotalBytes, _ => { }, CancellationToken.None);
            sw.Stop();

            Assert.Equal(TotalBytes, new FileInfo(dest).Length);
            Assert.True(handler.RequestCount("pace1.com") >= 2,
                "断流应在 watchdog 之前触发陪跑（pace1 第二次请求=陪跑开赛）");
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"耗时 {sw.Elapsed.TotalSeconds:F1}s——断流陪跑未生效");
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task PacePartsReuse_Chunked_ResumesFromBreakpoint()
    {
        // 分片档：被淘汰源已下部分片（评估间隔拉长到 3s 让它有分片积累），触发重赛后
        // Range 请求从断点偏移续传（起点 = 已下字节，非片边界/非 0）
        const int chunked = 2 * 1024 * 1024;
        var handler = new ScriptedSpeedHandler(chunked,
            ("win.com",
            [
                (20 * 1024, 1), (16 * 1024, 1), (12 * 1024, 1), (8 * 1024, 1), (4 * 1024, 1), (4 * 1024, 120),
            ]),
            ("pace1.com", [(2 * 1024, 4), (2 * 1024 * 1024, 60)]),
            ("pace2.com", [(1 * 1024, 60)]));
        var http = new HttpClient(handler);
        var resolver = new FixedResolver(["http://win.com/f.bin", "http://pace1.com/f.bin", "http://pace2.com/f.bin"]);
        var svc = new DownloadService(http, resolver, PaceOpts(evalMs: 3000), Path.GetTempPath(), (_, _) => Task.FromResult(true));
        var dest = Path.Combine(Path.GetTempPath(), $"pace-pr-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = Stopwatch.StartNew();
            await svc.DownloadFileAsync("http://win.com/f.bin", dest, null, chunked, _ => { }, CancellationToken.None);
            sw.Stop();

            Assert.Equal(chunked, new FileInfo(dest).Length);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(25), $"耗时 {sw.Elapsed.TotalSeconds:F1}s");
            // 断点续传：pace1 存在起点非 0 且非片边界的 Range 请求——即 .parts 断点复用证据
            var chunkSize = DownloadService.ChunkSizeFor(chunked);
            Assert.Contains(handler.RangeStarts("pace1.com"), s => s > 0 && s % chunkSize != 0);
        }
        finally { File.Delete(dest); }
    }

    /// <summary>轮询等待竞速残留清理（stragglers 后台清理异步，最多等 5s）</summary>
    private static async Task WaitRaceCleanAsync(string destPath, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var residue = Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(destPath)!,
                Path.GetFileName(destPath) + ".race*").ToList();
            if (residue.Count == 0) return;
            await Task.Delay(100);
        }
        Assert.Fail("竞速残留未清理");
    }

    // ---------- 桩 ----------

    private sealed class FixedResolver : IDlSourceResolver
    {
        private readonly string[] _urls;
        public FixedResolver(params string[] urls) => _urls = urls;
        public IReadOnlyList<string> Resolve(string officialUrl) => _urls;
    }

    /// <summary>按 (bytesPerSec, 秒数) 段表吐字节的流式桩；Range 感知（206 + 断点续传）；取消记录 host。
    /// 每个请求/连接独立重启段表（模拟服务器按连接限速）；Range 偏移只决定响应长度——
    /// 断点续传 = 客户端从断点追加，内容无校验（测试只校验大小）。</summary>
    private sealed class ScriptedSpeedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (int Bps, double Seconds)[]> _scripts;
        private readonly int _totalBytes;
        private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Cancelled = [];
        private readonly List<(string Host, long Start)> _ranges = [];

        public ScriptedSpeedHandler(int totalBytes, params (string Host, (int Bps, double Seconds)[] Script)[] hosts)
        {
            _totalBytes = totalBytes;
            _scripts = hosts.ToDictionary(h => h.Host, h => h.Script, StringComparer.OrdinalIgnoreCase);
        }

        public int RequestCount(string host) => _counts.TryGetValue(host, out var c) ? c : 0;

        public List<long> RangeStarts(string host)
        {
            lock (_ranges) return _ranges.Where(r => r.Host == host).Select(r => r.Start).ToList();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var host = request.RequestUri!.Host;
            lock (_counts) _counts[host] = RequestCount(host) + 1;
            if (!_scripts.TryGetValue(host, out var script))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            if (request.Headers.Range is { } r && r.Ranges.Count > 0)
            {
                var from = r.Ranges.First().From ?? 0;
                var to = r.Ranges.First().To ?? _totalBytes - 1;
                lock (_ranges) _ranges.Add((host, from));
                var resp = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ScriptedContent(script, to - from + 1, host, Cancelled),
                };
                resp.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, _totalBytes);
                return Task.FromResult(resp);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ScriptedContent(script, _totalBytes, host, Cancelled),
            });
        }
    }

    private sealed class ScriptedContent : HttpContent
    {
        private readonly (int Bps, double Seconds)[] _script;
        private readonly long _toEmit;
        private readonly string _host;
        private readonly List<string> _cancelled;

        public ScriptedContent((int Bps, double Seconds)[] script, long toEmit, string host, List<string> cancelled)
        {
            _script = script;
            _toEmit = toEmit;
            _host = host;
            _cancelled = cancelled;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new NotSupportedException();
        protected override bool TryComputeLength(out long length) { length = _toEmit; return true; }
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new ScriptedStream(_script, _toEmit, _host, _cancelled));
    }

    /// <summary>段表限速流：任一时刻允许发出的字节数 = 脚本对已流逝时间的积分（15ms 节流粒度）</summary>
    private sealed class ScriptedStream : Stream
    {
        private readonly (int Bps, double Seconds)[] _script;
        private readonly long _toEmit;
        private readonly string _host;
        private readonly List<string> _cancelled;
        private readonly long _startTicks = Environment.TickCount64;
        private long _emitted;

        public ScriptedStream((int Bps, double Seconds)[] script, long toEmit, string host, List<string> cancelled)
        {
            _script = script;
            _toEmit = toEmit;
            _host = host;
            _cancelled = cancelled;
        }

        private double Integral(double seconds)
        {
            double bytes = 0, t = 0;
            foreach (var (bps, sec) in _script)
            {
                if (seconds >= t + sec) bytes += bps * sec;
                else if (seconds > t) bytes += bps * (seconds - t);
                t += sec;
                if (t >= seconds) break;
            }
            return bytes;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            if (_emitted >= _toEmit) return 0;
            while (true)
            {
                var elapsed = (Environment.TickCount64 - _startTicks) / 1000.0;
                var allowed = Math.Min(Integral(elapsed), _toEmit);
                var want = (int)Math.Min(buffer.Length, Math.Max(0, allowed - _emitted));
                if (want > 0)
                {
                    _emitted += want;
                    return want;
                }
                if (_emitted >= _toEmit) return 0;
                try { await Task.Delay(15, ct); }
                catch (OperationCanceledException) { lock (_cancelled) _cancelled.Add(_host); throw; }
            }
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
    }
}
