using System.Collections.Specialized;
using Avalonia.Controls;
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

    /// <summary>点击头像 → 选择皮肤图片（png/jpg → 复制为本地头像）</summary>
    private async void OnAvatarClick(object? sender, RoutedEventArgs e)
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
