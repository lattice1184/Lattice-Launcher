using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.App.ViewModels;

using Launcher.App.Services;
namespace Launcher.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is HomeViewModel vm)
            {
                vm.GameLogs.CollectionChanged += OnLogsChanged;
                // 8-13 设备码自动复制：配对码一出现就进剪贴板（浏览器弹太快来不及手动复制）
                vm.Account.PropertyChanged += OnAccountPropertyChanged;
            }
        };
    }

    /// <summary>配对码生成即自动复制到剪贴板（DeviceCodeText 非空触发一次）</summary>
    private async void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AccountViewModel.DeviceCodeText)
            || sender is not AccountViewModel acc
            || acc.DeviceCodeText.Length == 0)
        {
            return;
        }
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is not { } cb) return;
        await cb.SetTextAsync(acc.DeviceCodeText);
        NotificationService.Success($"配对码 {acc.DeviceCodeText} 已复制，在浏览器粘贴即可");
    }

    /// <summary>新日志到达时控制台自动滚动到底部</summary>
    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => LogScroll?.ScrollToEnd());
    }

    /// <summary>8-13 复制设备码配对码（正版登录：浏览器输码页粘贴用）</summary>
    private async void OnCopyDeviceCode(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm || vm.Account.DeviceCodeText.Length == 0) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is not { } cb) return;
        await cb.SetTextAsync(vm.Account.DeviceCodeText);
        Launcher.App.Services.NotificationService.Success("配对码已复制");
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

    /// <summary>8-16 批次 51：打开内置 LittleSkin 皮肤库窗口（非模态；VM 独立实例）</summary>
    private void OnOpenSkinLibrary(object? sender, RoutedEventArgs e)
    {
        var win = new SkinLibraryWindow { DataContext = new ViewModels.SkinLibraryViewModel() };
        win.Show(DialogService.MainWindow());
    }

    /// <summary>8-14 重置皮肤（正版→强制同步官方皮肤；离线/Littleskin→随机默认）</summary>
    private void OnResetSkin(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HomeViewModel vm) _ = vm.ResetSkin();
    }

}
