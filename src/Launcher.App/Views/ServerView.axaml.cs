using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class ServerView : UserControl
{
    public ServerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ServerViewModel vm)
                vm.Logs.CollectionChanged += OnLogsChanged;
        };
    }

    /// <summary>服务端日志到达时控制台自动滚动到底部</summary>
    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => LogScroll?.ScrollToEnd());
    }

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Send();
    }

    private void OnSendClick(object? sender, RoutedEventArgs e) => Send();

    /// <summary>导出日志（游戏/崩溃日志 + 系统信息 zip）</summary>
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

    private void Send()
    {
        if (DataContext is not ServerViewModel vm) return;
        var box = this.FindControl<TextBox>("CommandBox");
        if (box is null) return;
        var cmd = box.Text;
        vm.SendCommandCommand.Execute(cmd);
        box.Text = "";
    }
}
