using Avalonia.Controls;
using Avalonia.Interactivity;
using Launcher.App.Services;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class SectionDownloadView : UserControl
{
    public SectionDownloadView() => InitializeComponent();

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    /// <summary>CF key 失焦验证：异步调 API 给反馈（结果只含状态，key 永不回显）</summary>
    private async void OnApiKeyLostFocus(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        await Vm.ValidateApiKeyAsync();
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
