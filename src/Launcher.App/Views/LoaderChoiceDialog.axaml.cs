using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
    private string _versionId = "";

    /// <summary>加载器版本列表（先 5 条 + 后台分批补全；增量绑定避免 ComboBox 全量重建卡顿）</summary>
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _versions = [];

    /// <summary>竞态丢弃：快速切换加载器时旧响应作废</summary>
    private int _versionGen;

    /// <summary>AL28 打开对话框即后台预取 Fabric 列表（最常用）；null = 预取失败（回退现场请求）</summary>
    private Task<List<LoaderMetaVersion>?>? _fabricPrefetch;

    public LoaderChoiceDialog()
    {
        InitializeComponent();
        global::Launcher.App.Animations.UiAnim.AttachDialog(this, Root);
    }

    /// <summary>展示加载器选择（versionId 为要下载的版本）；取消返回 null</summary>
    public static async Task<LoaderChoice?> ShowAsync(Window? owner, string versionId)
    {
        var win = new LoaderChoiceDialog { _versionId = versionId };
        win.VersionTitle.Text = $"下载 {versionId}";
        // AL28 预取 Fabric 版本列表：网络慢（meta.fabricmc.net 实测 12s+）时点 chip 直接秒出，不干等
        win._fabricPrefetch = Task.Run(async () =>
        {
            try { return await win._service.GetLoaderVersionsAsync(LoaderKind.Fabric, versionId, CancellationToken.None); }
            catch { return (List<LoaderMetaVersion>?)null; }
        });
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

        // 懒加载版本列表：先绑前 5 条立即可用，剩余后台分批静默补全（全量绑定 ComboBox 会卡）
        var gen = ++_versionGen;
        VersionStatus.Text = "加载版本…";
        _versions.Clear();
        VersionBox.ItemsSource = _versions;
        try
        {
            // Fabric：优先用打开对话框时的预取结果（失败回退现场请求）
            var list = _kind.Value == LoaderKind.Fabric && _fabricPrefetch is { } pf
                ? await pf ?? await _service.GetLoaderVersionsAsync(_kind.Value, _versionId, CancellationToken.None)
                : await _service.GetLoaderVersionsAsync(_kind.Value, _versionId, CancellationToken.None);
            if (gen != _versionGen) return; // 竞态：期间切了别的加载器，丢弃旧响应

            var versions = list.Select(v => v.Version).ToList();
            if (versions.Count == 0)
            {
                VersionStatus.Text = "该加载器暂无可用版本";
                return;
            }

            foreach (var v in versions.Take(5)) _versions.Add(v); // 前 5 条立即渲染
            VersionBox.SelectedItem = _versions[0];

            // 静默补全剩余（分批节流，UI 不卡；期间切加载器则丢弃）
            var rest = versions.Skip(5).ToList();
            for (var i = 0; i < rest.Count; i += 8)
            {
                if (gen != _versionGen) return;
                await Task.Delay(25); // 节流：给 UI 呼吸时间
                var batch = rest.Skip(i).Take(8).ToList();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (gen != _versionGen) return;
                    foreach (var v in batch) _versions.Add(v);
                });
            }
            VersionStatus.Text = $"共 {_versions.Count} 个版本";
        }
        catch (Exception ex)
        {
            if (gen == _versionGen) VersionStatus.Text = $"加载失败: {ex.Message}";
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
