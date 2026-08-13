using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.App.Services;
using Launcher.App.ViewModels;
using Launcher.Core.Download;
using Launcher.Core.Utils;

namespace Launcher.App.Views;

public partial class VersionBrowseView : UserControl
{
    public VersionBrowseView()
    {
        InitializeComponent();
    }

    private VersionBrowseViewModel? Vm => DataContext as VersionBrowseViewModel;

    /// <summary>顶部"下载游戏 →"：切到下载板块的下载游戏 tab</summary>
    private void OnGoDownload(object? sender, RoutedEventArgs e)
        => MainViewModel.Current?.NavigateToDownloadGame();

    /// <summary>左栏行 [▶]：直接启动该版本</summary>
    private void OnLaunchRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: InstalledVersionRowVM row })
            MainViewModel.Current?.LaunchVersion(row.Id, row.GameDir);
    }

    /// <summary>导入整合包（选 zip/mrpack → 统一入口：确认 → 下载中心组任务 → 版本页选中）。AL47 支持 CF/mrpack</summary>
    private async void OnImportModpack(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择整合包",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("整合包 (*.zip / *.mrpack)") { Patterns = ["*.zip", "*.mrpack"] },
            ],
        });
        if (files.Count == 0 || !files[0].Path.IsAbsoluteUri) return;
        ModpackImportFlow.StartAsync(files[0].Path.LocalPath);
    
}

}
