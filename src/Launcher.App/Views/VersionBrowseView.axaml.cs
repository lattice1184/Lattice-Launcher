using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class VersionBrowseView : UserControl
{
    public VersionBrowseView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachManagePicker();
    }

    /// <summary>版本管理面板创建时挂上"导出整合包"的目录选择回调（FolderPicker）</summary>
    private void AttachManagePicker()
    {
        if (DataContext is not VersionBrowseViewModel vm) return;
        vm.PickModpackFile = PickModpackFileAsync;
        vm.Detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Detail.Manage) && vm.Detail.Manage is { } m)
                m.PickFolder = PickFolderAsync;
        };
    }

    private async Task<string?> PickModpackFileAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择整合包",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("整合包") { Patterns = ["*.zip"] },
            ],
        });
        return files.Count > 0 && files[0].Path.IsAbsoluteUri ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> PickFolderAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择导出位置",
            AllowMultiple = false,
        });
        return folders.Count > 0 && folders[0].Path.IsAbsoluteUri ? folders[0].Path.LocalPath : null;
    }
}
