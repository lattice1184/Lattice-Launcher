using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.App.ViewModels;

using Launcher.App.Services;
namespace Launcher.App.Views;

public partial class SectionGameDirView : UserControl
{
    public SectionGameDirView() => InitializeComponent();

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private IStorageProvider? Picker => TopLevel.GetTopLevel(this)?.StorageProvider;

    private async void OnBrowseGameDir(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Picker is null) return;
        var folders = await Picker.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择游戏目录",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].Path.IsAbsoluteUri)
            Vm.ApplyGameDirectory(folders[0].Path.LocalPath);
    
}

    private void OnResetGameDir(object? sender, RoutedEventArgs e) => Vm?.ResetGameDirectory();

}
