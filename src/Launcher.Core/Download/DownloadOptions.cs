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

    /// <summary>整轮尝试数（每轮遍历全部候选源）</summary>
    public int MaxSourceAttempts { get; init; } = 3;

    /// <summary>官方源失败时启用镜像回退（BMCLAPI）</summary>
    public bool MirrorFallbackEnabled { get; init; } = true;

    /// <summary>全局下载限速（字节/秒；0 = 不限速）</summary>
    public long BytesPerSecond { get; init; }

    /// <summary>轮间退避（测试注入 0 加速；null → RetryPolicy.Backoff）</summary>
    public Func<int, TimeSpan>? BackoffProvider { get; init; }

    public static DownloadOptions Default { get; } = new();
}
