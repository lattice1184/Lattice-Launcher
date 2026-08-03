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

    /// <summary>导入整合包（选 zip → 确认 → 解压为实例）</summary>
    private async void OnImportModpack(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || Vm is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择整合包",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("整合包") { Patterns = ["*.zip"] }],
        });
        if (files.Count == 0 || !files[0].Path.IsAbsoluteUri) return;
        var file = files[0].Path.LocalPath;

        var info = ModpackImporter.Parse(file, out var reason);
        if (info is null)
        {
            NotificationService.Error(reason ?? "无法解析整合包");
            return;
        }
        var owner = DialogService.MainWindow();
        if (owner is null || !await DialogService.Confirm(owner,
                $"导入整合包「{info.VersionId}」？ MC {info.McVersion} · {info.FileCount} 个文件，将解压到版本目录。",
                "导入整合包", "导入", "取消"))
        {
            return;
        }

        try
        {
            var dir = GameDirectory.InstallDir();
            await Task.Run(() => ModpackImporter.Import(file, dir, CancellationToken.None));
            NotificationService.Success($"已导入 {info.VersionId}");
            Vm.OnInstalled(info.VersionId);
        }
        catch (Exception ex)
        {
            NotificationService.Error($"导入失败: {ex.Message}");
        }
    }
}
