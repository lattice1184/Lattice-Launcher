namespace Launcher.Core.Download;

/// <summary>
/// 源"死亡"检测抛出的异常——模拟源失败，让外层换路机制接管（单候选重试整轮 /
/// 竞速标记该源失败）。区别于网络异常：这是主动判定"源还在但已不可用"（免费镜像
/// 按 IP 烧完配额后：连接不断但速度趋近 0——实机 08-10：gh-proxy.com 从 2.76MB/s
/// 渐降到 12KB/s，任务停在原地不换路）。
/// </summary>
public sealed class SlowSourceException : HttpRequestException
{
    public SlowSourceException(long threshold, double speed)
        : base($"源速度持续低于 {threshold / 1024.0:0}KB/s（实测 {speed / 1024.0:0}KB/s）") { }
}

/// <summary>
/// 下载中"心率监测"（AL61）：下载循环每采样间隔读一次已下载字节 → 算实时速度；
/// 连续 SlowSamples 次低于阈值（默认 5s×6=30s < 100KB/s）→ 判定源死。
/// 窗口故意偏长：TCP 慢启动/镜像瞬时抖动/大文件波动都会造成短时低速，误杀换来换去更慢。
/// </summary>
public sealed class SlowSourceDetector
{
    private readonly long _thresholdBps;
    private readonly int _slowSamples;
    private readonly long _probeMs;
    private long _lastBytes;
    private long _lastTick;
    private int _slowCount;
    private bool _started;

    public SlowSourceDetector(long thresholdBps, int slowSamples, long probeMs)
    {
        _thresholdBps = thresholdBps;
        _slowSamples = Math.Max(2, slowSamples); // 至少 2 次采样确认——首采样会被 TCP 慢启动误导
        _probeMs = probeMs;
    }

    /// <summary>
    /// 喂入当前已下载字节（单连接 = read 计数；分片 = ChunkProgress.Bytes 总吞吐）。
    /// 返回 true = 判定源死，调用方应中止本源的下载（抛 SlowSourceException）。
    /// </summary>
    /// <summary>最近一次采样的速度（字节/秒；触发后报告用）</summary>
    public double LastSpeed { get; private set; }

    public bool ShouldAbort(long bytes, CancellationToken ct)
    {
        var now = Environment.TickCount64;
        if (!_started)
        {
            _started = true;
            _lastBytes = bytes;
            _lastTick = now;
            return false;
        }
        if (now - _lastTick < _probeMs) return false;
        var dt = (now - _lastTick) / 1000.0;
        var speed = dt > 0 ? (bytes - _lastBytes) / dt : 0;
        LastSpeed = speed;
        _lastBytes = bytes;
        _lastTick = now;
        if (speed < _thresholdBps)
            _slowCount++;
        else
            _slowCount = 0;
        return _slowCount >= _slowSamples;
    }
}
