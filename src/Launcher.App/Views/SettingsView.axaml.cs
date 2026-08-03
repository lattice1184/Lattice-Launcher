using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    // ---------- 游戏目录 ----------

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

    // ---------- Java ----------

    private async void OnBrowseJava(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Picker is null) return;
        var files = await Picker.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 java.exe",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Java 可执行文件") { Patterns = ["java.exe"] }],
        });
        if (files.Count > 0 && files[0].Path.IsAbsoluteUri)
            Vm.ApplyJavaPath(files[0].Path.LocalPath);
    }

    private void OnResetJava(object? sender, RoutedEventArgs e) => Vm?.ResetJavaPath();

    // ---------- 自定义内存 ----------

    private void OnMemoryCustomKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitCustomMemory();
    }

    private void OnMemoryCustomLostFocus(object? sender, RoutedEventArgs e) => CommitCustomMemory();

    private void CommitCustomMemory()
    {
        if (Vm is null) return;
        var box = this.FindControl<TextBox>("MemoryCustomText");
        Vm.ApplyCustomMemory(box?.Text ?? "");
    }
}
