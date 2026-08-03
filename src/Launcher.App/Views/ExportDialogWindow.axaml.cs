using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.App.Services;
using Launcher.Core.Download;
using Launcher.Core.Utils;

namespace Launcher.App.Views;

/// <summary>
/// 导出整合包设置对话框（PCL 式）：内容勾选 / 输出位置 / 包名描述。
/// 确认返回 ExportSettings；取消/关闭返回 null。
/// </summary>
public partial class ExportDialogWindow : Window
{
    private TaskCompletionSource<ExportSettings?>? _result;

    public ExportDialogWindow()
    {
        InitializeComponent();
    }

    /// <summary>展示导出设置框（defaultDir 默认输出目录；defaultName 默认包名）</summary>
    public static async Task<ExportSettings?> ShowAsync(Window? owner, string defaultName, string defaultDir)
    {
        var win = new ExportDialogWindow();
        win.NameBox.Text = defaultName;
        win.PathBox.Text = defaultDir;
        var tcs = new TaskCompletionSource<ExportSettings?>();
        win._result = tcs;
        if (owner is not null) await win.ShowDialog(owner);
        else win.Show();
        return await tcs.Task;
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择导出位置",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].Path.IsAbsoluteUri)
            PathBox.Text = folders[0].Path.LocalPath;
    }

    private void OnExport(object? sender, RoutedEventArgs e)
    {
        var dir = PathBox.Text?.Trim() ?? "";
        if (dir.Length == 0)
        {
            PathBox.Text = GameDirectory.InstallDir();
            dir = PathBox.Text;
        }
        // 包名清洗（非法文件名字符 → 下划线）
        var name = ModpackImporter.SafeId(NameBox.Text?.Trim() ?? "");
        _result?.TrySetResult(new ExportSettings(
            IncludeMods.IsChecked == true,
            IncludeSaves.IsChecked == true,
            IncludeConfig.IsChecked == true,
            IncludeResourcepacks.IsChecked == true,
            IncludeShaders.IsChecked == true,
            IncludeOptions.IsChecked == true,
            dir,
            name,
            DescBox.Text?.Trim() ?? ""));
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result?.TrySetResult(null);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _result?.TrySetResult(null); // X/Alt+F4 兜底
        base.OnClosed(e);
    }
}
