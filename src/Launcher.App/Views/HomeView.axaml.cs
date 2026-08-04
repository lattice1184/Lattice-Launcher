using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is HomeViewModel vm)
                vm.GameLogs.CollectionChanged += OnLogsChanged;
        };
    }

    /// <summary>新日志到达时控制台自动滚动到底部</summary>
    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => LogScroll?.ScrollToEnd());
    }

    /// <summary>复制控制台全部日志到剪贴板（错误信息可直接粘贴给他人）</summary>
    private async void OnCopyLogs(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm || vm.GameLogs.Count == 0)
        {
            Launcher.App.Services.NotificationService.Error("控制台暂无日志");
            return;
        }
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is not { } cb) return;
        await cb.SetTextAsync(string.Join(Environment.NewLine, vm.GameLogs));
        Launcher.App.Services.NotificationService.Success($"已复制 {vm.GameLogs.Count} 行日志");
    }

    /// <summary>导出日志（游戏日志 + 崩溃日志 + 系统信息 → zip）</summary>
    private async void OnExportLogs(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择日志保存位置",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || !folders[0].Path.IsAbsoluteUri) return;
        try
        {
            var path = await Task.Run(() => Launcher.App.Services.LogExportHelper.ExportLogs(folders[0].Path.LocalPath));
            Launcher.App.Services.NotificationService.Success($"日志已导出：{Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Launcher.App.Services.NotificationService.Error($"导出失败: {ex.Message}");
        }
    }

    /// <summary>点击头像 → 切换账号面板（Popup）</summary>
    private void OnAvatarClick(object? sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = !AccountPopup.IsOpen;
    }

    /// <summary>账号面板弹出 → 弹性放大进入（BackEase overshoot）</summary>
    private void OnAccountPopupOpened(object? sender, EventArgs e)
    {
        Animations.UiAnim.SpringIn(AccountPanel);
    }

    /// <summary>账号列表"切换"（Popup 内 $parent 绑定不可靠，走 code-behind 转发）</summary>
    private void OnSwitchAccount(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm || (sender as Button)?.DataContext is not AccountRowVM row) return;
        vm.Account.SwitchAccountCommand.Execute(row);
    }

    /// <summary>账号列表"删除"（确认对话框在命令内）</summary>
    private async void OnDeleteAccount(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm || (sender as Button)?.DataContext is not AccountRowVM row) return;
        await vm.Account.DeleteAccountCommand.ExecuteAsync(row);
    }

    /// <summary>更换皮肤 → 选择图片（png/jpg → 复制为本地头像）</summary>
    private async void OnPickSkin(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择皮肤图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg"] },
            ],
        });
        if (files.Count > 0 && files[0].Path.IsAbsoluteUri)
            vm.ApplyLocalSkin(files[0].Path.LocalPath);
    }
}
