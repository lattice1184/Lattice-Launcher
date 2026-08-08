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
