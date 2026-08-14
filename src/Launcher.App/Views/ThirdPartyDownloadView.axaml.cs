using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.App.ViewModels;

using Launcher.App.Services;
namespace Launcher.App.Views;

public partial class ThirdPartyDownloadView : UserControl
{
    public ThirdPartyDownloadView() => InitializeComponent();

    private async void OnChooseDir(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThirdPartyDownloadViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { } picker) return;
        var folders = await picker.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择下载目录",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].Path.IsAbsoluteUri)
            vm.ApplyDir(folders[0].Path.LocalPath);
    
}

}
