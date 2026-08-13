using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Launcher.App.Services;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class SectionDownloadView : UserControl
{
    public SectionDownloadView() => InitializeComponent();

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    /// <summary>验证 CurseForge API Key：先提交输入框内容（有输入才覆盖），再直连验证（结果只含状态，key 永不回显）</summary>
    private async void OnCheckProxy(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        await Vm.SubmitApiKeyAsync();
    }

    /// <summary>打开 CurseForge API 控制台（PCL 式「获取 API」引导：Google 登录，申请表单必填网站地址 + Git 地址）</summary>
    private void OnOpenCfConsole(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://console.curseforge.com/") { UseShellExecute = true }); }
        catch { NotificationService.Error("无法打开浏览器，请手动访问 console.curseforge.com"); }
    }

    /// <summary>清理下载缓存：删 *.parts 残留，Toast 报告释放空间</summary>
    private void OnClearDownloadCache(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var (dirs, bytes) = Vm.ClearDownloadCache();
        if (dirs == 0)
        {
            NotificationService.Info("没有需要清理的下载缓存");
            return;
        }
        var size = bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{bytes / 1024.0:0} KB";
        NotificationService.Success($"已清理 {dirs} 个临时目录，释放 {size}");
    
}

}
