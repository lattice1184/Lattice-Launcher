using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Launcher.App.Services;
using Launcher.Core.Utils;

namespace Launcher.App.Views;

/// <summary>
/// 崩溃报告窗口（PCL 式错误窗口）：错误信息 + 日志预览 + 导出错误报告（zip）。
/// </summary>
public partial class CrashReportWindow : Window
{
    private string _error = "";

    public CrashReportWindow()
    {
        InitializeComponent();
    }

    /// <summary>展示崩溃窗口（主窗口存在时作为模态；否则独立）</summary>
    public static void Show(string error) => Show("启动器遇到问题", error, RecentLogs());

    /// <summary>展示崩溃窗口（自定义标题/错误/日志预览——游戏崩溃与启动器崩溃共用）</summary>
    public static void Show(string title, string error, string logPreview)
    {
        var win = new CrashReportWindow { _error = error };
        win.Title = title;
        win.ErrorText.Text = error;
        win.LogPreview.Text = logPreview;
        if (Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
            && main.PlatformImpl is not null && main.IsVisible)
        {
            try { win.ShowDialog(main); return; }
            catch { /* 兜底独立窗口 */ }
        }
        win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        win.Show();
    }

    /// <summary>最近错误日志尾部（AppData\Launcher\logs\crash-*.log 最新 3 个，各尾部 40 行）</summary>
    private static string RecentLogs()
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "logs");
            if (!Directory.Exists(logDir)) return "（无日志）";
            var files = Directory.EnumerateFiles(logDir, "crash-*.log")
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc).Take(3).ToList();
            if (files.Count == 0) return "（无日志）";
            var sb = new StringBuilder();
            foreach (var f in files)
            {
                sb.AppendLine($"===== {Path.GetFileName(f)} =====");
                var lines = File.ReadAllLines(f);
                foreach (var line in lines.Skip(Math.Max(0, lines.Length - 40)))
                    sb.AppendLine(line);
            }
            return sb.ToString();
        }
        catch { return "（日志读取失败）"; }
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择报告保存位置",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || !folders[0].Path.IsAbsoluteUri) return;
        var outDir = folders[0].Path.LocalPath;
        var zipPath = Path.Combine(outDir, $"YanKa-错误报告-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        try
        {
            await Task.Run(() =>
            {
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                // 1. 错误信息
                var err = zip.CreateEntry("错误信息.txt");
                using (var sw = new StreamWriter(err.Open(), new UTF8Encoding(false)))
                    sw.Write(_error + Environment.NewLine + LogExportHelper.SystemInfo());
                // 2. 最近崩溃日志 + 游戏日志
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "logs");
                if (Directory.Exists(logDir))
                {
                    foreach (var f in Directory.EnumerateFiles(logDir, "crash-*.log")
                                 .OrderByDescending(x => new FileInfo(x).LastWriteTimeUtc).Take(3))
                    {
                        zip.CreateEntryFromFile(f, $"logs/{Path.GetFileName(f)}");
                    }
                    foreach (var f in Directory.EnumerateFiles(logDir, "launch-*.log")
                                 .OrderByDescending(x => new FileInfo(x).LastWriteTimeUtc).Take(2))
                    {
                        zip.CreateEntryFromFile(f, $"logs/{Path.GetFileName(f)}");
                    }
                }
                // 3. 设置（不含账号 token）
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "settings.json");
                if (File.Exists(settingsPath))
                    zip.CreateEntryFromFile(settingsPath, "settings.json");
            });
            ErrorText.Text += Environment.NewLine + $"报告已导出：{zipPath}";
        }
        catch (Exception ex)
        {
            ErrorText.Text += Environment.NewLine + $"导出失败：{ex.Message}";
        }
    }

    /// <summary>系统信息（OS/内存/CPU/启动器版本）</summary>
    private static string SystemInfo()
        => Environment.NewLine
           + "----- 系统信息 -----" + Environment.NewLine
           + $"系统：{Environment.OSVersion}" + Environment.NewLine
           + $"CPU：{Environment.ProcessorCount} 核" + Environment.NewLine
           + $"可用内存：{GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024} MB" + Environment.NewLine
           + $"启动器：YanKa Launcher" + Environment.NewLine
           + $"游戏目录：{GameDirectory.InstallDir()}" + Environment.NewLine;

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Avalonia 12 剪贴板 API 大改（SetDataAsync）——用 Windows 自带 clip.exe 写剪贴板（可靠简单）
            await Task.Run(() =>
            {
                var psi = new System.Diagnostics.ProcessStartInfo("clip.exe")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                p.StandardInput.Write(_error + Environment.NewLine + SystemInfo());
                p.StandardInput.Close();
                p.WaitForExit(3000);
            });
            CopyBtn.Content = "已复制 ✓";
        }
        catch { }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
