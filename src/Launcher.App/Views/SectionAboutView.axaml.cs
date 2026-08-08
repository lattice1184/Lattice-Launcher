using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Launcher.App.Views;

public partial class SectionAboutView : UserControl
{
    public SectionAboutView() => InitializeComponent();

    /// <summary>打开存储空间窗口（列出全部启动器文件位置与占用，可清理）</summary>
    private void OnOpenStorage(object? sender, RoutedEventArgs e)
    {
        var win = new StorageWindow();
        if (Launcher.App.Services.DialogService.MainWindow() is { } owner && owner.IsVisible)
            win.ShowDialog(owner);
        else
            win.Show();
    }

    /// <summary>
    /// 卸载启动器：红字警告列出删除项 → 写延迟删除 ps1（UTF-8 BOM，中文路径不乱码）→ 退出进程。
    /// 安装目录只删 exe + 空目录（不带 -Recurse，防误删用户自放文件）；应用数据与游戏目录递归全删。
    /// </summary>
    private async void OnUninstall(object? sender, RoutedEventArgs e)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Launcher.App.Services.NotificationService.Error("无法定位启动器路径");
            return;
        }
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher");
        var gameDir = Launcher.Core.Utils.LauncherSettings.Current.GameDirectory ?? Launcher.Core.Utils.GameDirectory.Detect();
        var installDir = Path.GetDirectoryName(exePath) ?? "";

        var owner = Launcher.App.Services.DialogService.MainWindow();
        if (owner is null) return;
        var ok = await Launcher.App.Services.DialogService.Warn(owner,
            "将永久删除启动器及其全部数据",
            "删除内容：\n· 启动器本体：" + exePath
            + "\n· 应用数据（设置/账号/日志）：" + appDataDir
            + "\n· 游戏目录（含世界存档）：" + gameDir
            + "\n\n此操作不可恢复，确认卸载？",
            "卸载启动器", "确认卸载", "取消");
        if (!ok) return;

        try
        {
            var ps = Path.Combine(Path.GetTempPath(), "yanla_uninstall.ps1");
            var content = "Start-Sleep -Seconds 3\r\n"
                + $"Remove-Item -LiteralPath '{exePath}' -Force -ErrorAction SilentlyContinue\r\n"
                + $"Remove-Item -LiteralPath '{installDir}' -Force -ErrorAction SilentlyContinue\r\n" // 仅空目录
                + $"Remove-Item -LiteralPath '{appDataDir}' -Recurse -Force -ErrorAction SilentlyContinue\r\n"
                + $"Remove-Item -LiteralPath '{gameDir}' -Recurse -Force -ErrorAction SilentlyContinue\r\n"
                + "Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue\r\n";
            File.WriteAllText(ps, content, new System.Text.UTF8Encoding(true)); // BOM：PowerShell 5.1 正确识别 UTF-8
            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{ps}\"")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            // 关闭主窗口（应用随之退出）；延迟删除脚本 3 秒后清理本体
            Launcher.App.Services.DialogService.MainWindow()?.Close();
        }
        catch (Exception ex)
        {
            Launcher.App.Services.NotificationService.Error($"卸载失败: {ex.Message}");
        }
    }
}
