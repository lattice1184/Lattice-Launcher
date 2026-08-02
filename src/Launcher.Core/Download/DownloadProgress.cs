namespace Launcher.Core.Download;

/// <summary>
/// 下载进度快照：阶段文字 + 当前文件 + 文件级字节 + 整体百分比（0-100）。
/// 低层 DownloadService 只填文件级字节；编排层（DownloadVersionAsync 等）补充阶段与整体百分比。
/// </summary>
public sealed record DownloadProgress(
    string Stage,
    string? CurrentFile,
    long FileBytesDone,
    long FileTotalBytes,
    double OverallPercent);

public delegate void DownloadProgressHandler(DownloadProgress progress);
