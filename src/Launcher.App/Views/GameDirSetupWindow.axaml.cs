using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Launcher.Core.Utils;

namespace Launcher.App.Views;

/// <summary>
/// 首次启动的游戏目录询问窗：选择游戏版本/模组/存档的存放文件夹。
/// 确认后写入 settings.json（LauncherSettings.GameDirectory），此后不再询问。
/// </summary>
public partial class GameDirSetupWindow : Window
{
    public GameDirSetupWindow()
    {
        InitializeComponent();
        PathBox.Text = GameDirectory.InstallDir();
    }

    /// <summary>浏览…：系统文件夹选择器</summary>
    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择游戏目录",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].Path.IsAbsoluteUri)
            PathBox.Text = folders[0].Path.LocalPath;
    }

    /// <summary>使用默认目录：重置为自建目录（D 盘优先）</summary>
    private void OnReset(object? sender, RoutedEventArgs e) => PathBox.Text = GameDirectory.OwnDefault();

    /// <summary>开始使用：保存设置并关闭</summary>
    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        var dir = PathBox.Text?.Trim() ?? "";
        if (dir.Length > 0)
        {
            var s = LauncherSettings.Current;
            s.GameDirectory = dir;
            s.Save();
        }
        Close();
    }
}
