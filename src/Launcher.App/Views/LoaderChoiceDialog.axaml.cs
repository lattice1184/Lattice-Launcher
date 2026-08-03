using Avalonia.Controls;
using Avalonia.Interactivity;
using Launcher.Core.Download;
using Launcher.Core.Model.Loader;

namespace Launcher.App.Views;

/// <summary>下载前选择的加载器（null LoaderKind = 纯净原版）</summary>
public sealed record LoaderChoice(LoaderKind? Kind, string? Version)
{
    public bool IsVanilla => Kind is null;
}

/// <summary>
/// 下载加载器选择对话框（PCL 式）：纯净原版 / 四家加载器 + 版本下拉，[开始下载] 才下载。
/// </summary>
public partial class LoaderChoiceDialog : Window
{
    private readonly LoaderService _service = new();
    private TaskCompletionSource<LoaderChoice?>? _result;
    private LoaderKind? _kind;

    public LoaderChoiceDialog()
    {
        InitializeComponent();
    }

    /// <summary>展示加载器选择（versionId 为要下载的版本）；取消返回 null</summary>
    public static async Task<LoaderChoice?> ShowAsync(Window? owner, string versionId)
    {
        var win = new LoaderChoiceDialog();
        win.VersionTitle.Text = $"下载 {versionId}";
        var tcs = new TaskCompletionSource<LoaderChoice?>();
        win._result = tcs;
        if (owner is not null) await win.ShowDialog(owner);
        else win.Show();
        return await tcs.Task;
    }

    /// <summary>加载器 chips 点击（Tag=加载器名；空 = 纯净原版）</summary>
    private async void OnLoaderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        // 高亮
        foreach (var b in new[] { VanillaBtn, FabricBtn, ForgeBtn, NeoForgeBtn, QuiltBtn })
        {
            if (b is not null) b.Classes.Set("active", b == btn);
        }

        var tag = (string?)btn.Tag ?? "";
        _kind = tag.Length == 0 ? null : Enum.Parse<LoaderKind>(tag);
        VersionBox.SelectedItem = null;
        VersionPanel.IsVisible = _kind is not null;
        if (_kind is null)
        {
            VersionStatus.Text = "";
            return;
        }

        // 懒加载版本列表
        VersionStatus.Text = "加载版本…";
        VersionBox.ItemsSource = null;
        try
        {
            var list = await _service.GetLoaderVersionsAsync(_kind.Value, "", CancellationToken.None);
            var versions = list.Select(v => v.Version).ToList();
            VersionBox.ItemsSource = versions;
            if (versions.Count > 0)
            {
                VersionBox.SelectedItem = versions[0];
                VersionStatus.Text = $"共 {versions.Count} 个版本";
            }
            else
            {
                VersionStatus.Text = "该加载器暂无可用版本";
            }
        }
        catch (Exception ex)
        {
            VersionStatus.Text = $"加载失败: {ex.Message}";
        }
    }

    private void OnStart(object? sender, RoutedEventArgs e)
    {
        if (_kind is { } kind)
        {
            if (VersionBox.SelectedItem is not string ver || ver.Length == 0)
            {
                VersionStatus.Text = "请选择加载器版本";
                return;
            }
            _result?.TrySetResult(new LoaderChoice(kind, ver));
        }
        else
        {
            _result?.TrySetResult(new LoaderChoice(null, null));
        }
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
