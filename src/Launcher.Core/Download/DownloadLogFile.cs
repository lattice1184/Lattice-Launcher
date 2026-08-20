using PCL.Core.Logging;

namespace Launcher.Core.Download;

/// <summary>
/// 8-20 下载日志落盘：订阅 LogWrapper 事件 → 写 %AppData%\Launcher\logs\download.log。
/// 简洁逐行（[HH:mm:ss] 消息），只记 Info+（候选源/完成/失败/判死/换源——进度类 Debug 不写，
/// 高频噪音不进日志）。启动时 >5MB 轮转（清空重写）。同步写（日志低频，无需异步队列）。
/// </summary>
public static class DownloadLogFile
{
    private static readonly object Gate = new();
    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Launcher", "logs", "download.log");

    private static bool _attached;

    /// <summary>启动时调用一次：订阅事件并开始落盘（幂等）</summary>
    public static void Attach()
    {
        lock (Gate)
        {
            if (_attached) return;
            _attached = true;
            try
            {
                var dir = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(dir);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 5 * 1024 * 1024) File.WriteAllText(LogPath, ""); // 轮转
            }
            catch { /* 日志失败不影响启动 */ }
            LogWrapper.OnLog += (level, msg, module, ex) =>
            {
                if (level < LogLevel.Info) return; // 简洁：只落 Info+
                try
                {
                    lock (Gate)
                    {
                        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {msg}";
                        if (ex is not null) line += $" | {ex.GetType().Name}: {ex.Message}";
                        File.AppendAllText(LogPath, line + "\n");
                    }
                }
                catch { /* 日志写入失败无妨 */ }
            };
        }
    }
}
