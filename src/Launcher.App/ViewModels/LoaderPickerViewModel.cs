using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Download;
using Launcher.Core.Model.Loader;

namespace Launcher.App.ViewModels;

/// <summary>
/// 版本行的加载器安装面板：四家加载器选择 → 版本列表（最新置顶）→ 安装（全局下载中心）。
/// </summary>
public partial class LoaderPickerViewModel : ViewModelBase
{
    private readonly LoaderService _service;
    private readonly string _mcVersion;
    private readonly Action _onInstalled;
    private LoaderKind _kind = LoaderKind.Fabric;

    public ObservableCollection<LoaderOptionVM> Loaders { get; } = [];
    public ObservableCollection<string> Versions { get; } = [];

    [ObservableProperty]
    public partial LoaderOptionVM? SelectedLoader { get; set; }

    [ObservableProperty]
    public partial string? SelectedVersion { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "选择加载器";

    [ObservableProperty]
    public partial bool IsLoadingVersions { get; set; }

    [ObservableProperty]
    public partial bool IsInstalling { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    public LoaderPickerViewModel(string mcVersion, string gameDirectory, Action onInstalled)
    {
        _mcVersion = mcVersion;
        _onInstalled = onInstalled;
        _service = new LoaderService(gameDirectory: gameDirectory);
        Loaders.Add(new LoaderOptionVM("Fabric", LoaderKind.Fabric));
        Loaders.Add(new LoaderOptionVM("Quilt", LoaderKind.Quilt));
        Loaders.Add(new LoaderOptionVM("Forge", LoaderKind.Forge));
        Loaders.Add(new LoaderOptionVM("NeoForge", LoaderKind.NeoForge));
        SelectedLoader = Loaders[0];
    }

    partial void OnSelectedLoaderChanged(LoaderOptionVM? value)
    {
        if (value is null) return;
        _kind = value.Kind;
        _ = LoadVersionsAsync();
    }

    [RelayCommand]
    private void Select(string kind)
    {
        SelectedLoader = Loaders.FirstOrDefault(l =>
            l.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>加载该加载器可用版本（最新在前，最多展示 8 个）</summary>
    private int _versionGen;

    public async Task LoadVersionsAsync()
    {
        if (IsLoadingVersions) return;
        IsLoadingVersions = true;
        StatusText = "查询版本中…";
        var gen = ++_versionGen;
        try
        {
            Versions.Clear();
            var list = await _service.GetLoaderVersionsAsync(_kind, _mcVersion, CancellationToken.None);
            if (gen != _versionGen) return; // 快速切换加载器：旧响应丢弃
            foreach (var v in list.Take(8)) Versions.Add(v.Version);
            if (Versions.Count == 0)
            {
                StatusText = $"该版本暂无可用 {_kind} 版本";
                return;
            }
            SelectedVersion = Versions[0];
            StatusText = list.Count > 8 ? $"共 {list.Count} 个版本（显示前 8 个）" : $"共 {list.Count} 个版本";
        }
        catch (Exception ex)
        {
            StatusText = $"查询失败: {ex.Message}";
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    [RelayCommand]
    private async Task Install()
    {
        if (IsInstalling) return;
        var lv = SelectedVersion;
        if (string.IsNullOrEmpty(lv)) return;
        IsInstalling = true;
        StatusText = "准备中…";
        try
        {
            var plan = await _service.CreatePlanAsync(_kind, _mcVersion, lv, CancellationToken.None);
            var task = DownloadManager.Instance.Enqueue($"安装 {_kind} {lv} → {_mcVersion}",
                (p, ct) => _service.InstallAsync(plan, p, ct));

            void Sync(object? _, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(DownloadTask.ProgressPercent)) ProgressPercent = task.ProgressPercent;
                if (e.PropertyName == nameof(DownloadTask.Stage)) StatusText = task.Stage;
                if (e.PropertyName == nameof(DownloadTask.State)) StatusText = task.StateText;
            }
            task.PropertyChanged += Sync;
            try { await task.Completion; }
            finally { task.PropertyChanged -= Sync; }

            if (task.State == DownloadTaskState.Completed)
            {
                StatusText = $"已安装 {_kind} {lv}";
                _onInstalled();
            }
            else if (task.Error is { } err)
            {
                StatusText = err;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"安装失败: {ex.Message}";
        }
        finally
        {
            IsInstalling = false;
        }
    }
}

public sealed record LoaderOptionVM(string Display, LoaderKind Kind);
