using System.IO.Compression;
using System.Text;

namespace Launcher.App.Services;

/// <summary>日志导出共享工具：游戏日志 + 崩溃日志 + 系统信息 → zip</summary>
public static class LogExportHelper
{
    /// <summary>导出日志 zip（latest 游戏日志 + 最近 3 个崩溃日志 + 系统信息）→ 路径；失败抛异常</summary>
    public static string ExportLogs(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "logs");
        var zipPath = Path.Combine(outDir, $"YanKa-日志-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // 1. 系统信息
        var sys = zip.CreateEntry("系统信息.txt");
        using (var sw = new StreamWriter(sys.Open(), new UTF8Encoding(false)))
            sw.Write(SystemInfo());

        // 2. 最近游戏日志（launch-*.log 最新 2 个）
        if (Directory.Exists(logDir))
        {
            var launches = Directory.EnumerateFiles(logDir, "launch-*.log")
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc).Take(2).ToList();
            foreach (var f in launches)
                zip.CreateEntryFromFile(f, $"日志/{Path.GetFileName(f)}");

            // 3. 崩溃日志（crash-*.log 最近 3 个）
            foreach (var f in Directory.EnumerateFiles(logDir, "crash-*.log")
                         .OrderByDescending(x => new FileInfo(x).LastWriteTimeUtc).Take(3))
            {
                zip.CreateEntryFromFile(f, $"崩溃/{Path.GetFileName(f)}");
            }
        }

        return zipPath;
    }

    /// <summary>系统信息（OS/CPU/内存/目录）</summary>
    public static string SystemInfo()
        => "----- 系统信息 -----" + Environment.NewLine
           + $"系统：{Environment.OSVersion}" + Environment.NewLine
           + $"CPU：{Environment.ProcessorCount} 核" + Environment.NewLine
           + $"可用内存：{GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024} MB" + Environment.NewLine
           + $"启动器：YanKa Launcher" + Environment.NewLine
           + $"游戏目录：{Launcher.Core.Utils.GameDirectory.InstallDir()}" + Environment.NewLine;
}
