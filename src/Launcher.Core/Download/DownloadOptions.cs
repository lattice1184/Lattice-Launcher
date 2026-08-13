using Launcher.Core.Utils;

namespace Launcher.Core.Download;

/// <summary>
/// 下载性能配置（PCL2 参考：库并发 63 上限；本启动器默认保守合理值）。
/// </summary>
public sealed class DownloadOptions
{
    /// <summary>库文件并行数</summary>
    public int LibraryConcurrency { get; init; } = 8;

    /// <summary>资源文件并行数</summary>
    public int AssetConcurrency { get; init; } = 16;

    /// <summary>大文件分片连接数</summary>
    public int ChunkCount { get; init; } = 8;

    /// <summary>分片读取缓冲区（字节）</summary>
    public int BufferSize { get; init; } = 81920;

    /// <summary>整轮尝试数（每轮遍历全部候选源；2 轮足够——连接 15s 超时 + 0.5s 退避下轮间开销极低）</summary>
    public int MaxSourceAttempts { get; init; } = 2;

    /// <summary>下载源策略（官方优先 / 镜像优先 / 仅镜像）</summary>
    public DownloadSourcePreference DownloadSource { get; init; } = DownloadSourcePreference.OfficialFirst;

    /// <summary>全局下载限速（字节/秒；0 = 不限速）</summary>
    public long BytesPerSecond { get; init; }

    /// <summary>轮间退避（测试注入 0 加速；null → RetryPolicy.Backoff）</summary>
    public Func<int, TimeSpan>? BackoffProvider { get; init; }

    /// <summary>竞速淘汰评估间隔（AL59：到点无赢家 → 取消非领先源；测试注入短值加速）</summary>
    public TimeSpan RaceEliminateInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>源死亡判定：实时速度持续低于此值（字节/秒；AL61 下载中自动换源）</summary>
    public long SlowSpeedBps { get; init; } = 100 * 1024;

    /// <summary>
    /// 响应头超时（毫秒，AL64）：TCP 半开连接上 SendAsync(ResponseHeadersRead) 永不返回
    /// → 子任务卡死 → 组任务 WhenAll 挂 10 小时「下载中」（真机 08-11：26.2+Fabric 148.2/148.5MB
    /// 满速卡死）。响应头 N 秒拿不到判源死换路；body 下载不受限（大文件慢网继续）。
    /// </summary>
    public long ResponseHeaderTimeoutMs { get; init; } = 30000;

    /// <summary>
    /// body 读心跳超时（毫秒，AL66）：每次数据到达重置 N 秒定时器，ReadAsync 挂起
    /// （TCP 半开静默——响应头到了、body 中途断流）→ 超时判源死抛可重试错误换路。
    /// 修复 AL61 慢速检测挂在数据循环体内、读挂起时永不执行的洞（真机 08-11：fabric-api
    /// 单候选卡 0.2MB 3 分钟+——头超时管不到、慢速检测跑不到、单候选无竞速）。
    /// </summary>
    public long ReadStallTimeoutMs { get; init; } = 30000;

    /// <summary>源死亡判定：采样间隔（毫秒）</summary>
    public long SlowProbeMs { get; init; } = 5000;

    /// <summary>源死亡判定：连续低速采样数（默认 5s×6 = 持续 30s 龟速才判死——窗口偏长防误杀）</summary>
    public int SlowSamples { get; init; } = 6;

    public static DownloadOptions Default { get; } = new();

    /// <summary>按设置生成：并发档位 → 分片/库/资源并发；MaxConcurrentDownloads 优先于档位（改动即时生效）</summary>
    public static DownloadOptions FromSettings(LauncherSettings s)
    {
        var tier = (int)s.DownloadTier;
        return new DownloadOptions
        {
            ChunkCount = s.ChunkCount > 0 ? s.ChunkCount : tier,
            BufferSize = s.BufferSize > 0 ? s.BufferSize : 81920,
            LibraryConcurrency = s.MaxConcurrentDownloads > 0 ? s.MaxConcurrentDownloads : tier,
            AssetConcurrency = s.MaxConcurrentDownloads > 0 ? Math.Max(s.MaxConcurrentDownloads * 2, 16) : tier * 2,
            DownloadSource = s.DownloadSource,
            BytesPerSecond = s.DownloadSpeedLimitKbps > 0 ? s.DownloadSpeedLimitKbps * 1024 : 0,
        };
    }
}
