using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Download;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

/// <summary>
/// 版本列表：全部 / 正式版（前 10 折叠展开）/ 快照 / 愚人节 / 远古 分组，行内下载 + 进度。
/// </summary>
public partial class VersionListViewModel : ViewModelBase
{
    private readonly VersionManifestService _svc;
    private readonly VersionInstaller _installer;

    public ObservableCollection<VersionGroupVM> Groups { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "加载中…";

    public VersionListViewModel()
    {
        _svc = new VersionManifestService();
        _installer = new VersionInstaller();
    }

    public async Task LoadAsync()
    {
        try
        {
            await _svc.RefreshAsync();
            Groups.Clear();

            var all = _svc.Entries.ToList();
            var release = all.Where(e => e.Type == "release" && !IsAprilFools(e)).ToList();
            var snapshot = all.Where(e => e.Type == "snapshot" && !IsAprilFools(e)).ToList();
            var april = all.Where(IsAprilFools).ToList();
            var ancient = all.Where(e => e.Type is "old_alpha" or "old_beta").ToList();

            Groups.Add(new VersionGroupVM($"全部 ({all.Count})", all.Select(ToVm), isCollapsible: false));
            Groups.Add(new VersionGroupVM($"正式版 ({release.Count})", release.Select(ToVm), isCollapsible: true));
            Groups.Add(new VersionGroupVM($"快照 ({snapshot.Count})", snapshot.Select(ToVm)));
            Groups.Add(new VersionGroupVM($"愚人节 ({april.Count})", april.Select(ToVm)));
            Groups.Add(new VersionGroupVM($"远古 ({ancient.Count})", ancient.Select(ToVm)));

            Status = $"共 {all.Count} 个版本 · 点击下载安装到本地";
        }
        catch (Exception ex)
        {
            Status = $"加载失败: {ex.Message}";
        }
    }

    /// <summary>愚人节版本识别：4/1 前后发布的 + 特征 id（potato/craftmine/mob/combat 等）</summary>
    public static bool IsAprilFools(VersionManifestService.GameVersionEntry e)
    {
        if (e.ReleaseTime is { Month: 4, Day: <= 3 }) return true;
        var id = e.Id.ToLowerInvariant();
        return id.Contains("potato") || id.Contains("craftmine") || id.Contains("mob")
            || id.Contains("combat") || id.Contains("21w14") || id.Contains("25w14");
    }

    private VersionEntryVM ToVm(VersionManifestService.GameVersionEntry e) =>
        new(e.Id, e.Type, e.Installed, e.ReleaseTime.ToString("yyyy-MM-dd"), e.ManifestUrl, _installer, OnInstalled);

    /// <summary>安装完成：重扫磁盘并点亮所有已装行（含其他分组）</summary>
    private void OnInstalled(VersionEntryVM entry)
    {
        _svc.RescanInstalled();
        var installedSet = new HashSet<string>(
            _svc.Entries.Where(e => e.Installed).Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var g in Groups)
            foreach (var item in g.All)
                if (installedSet.Contains(item.Id)) item.Installed = true;
    }
}

/// <summary>版本分组（正式版组支持前 10 折叠展开）</summary>
public partial class VersionGroupVM : ObservableObject
{
    private const int CollapsedLimit = 10;
    private readonly List<VersionEntryVM> _all;

    public string Title { get; }
    public int Total { get; }
    public bool IsCollapsible { get; }
    public IReadOnlyList<VersionEntryVM> All => _all;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public IEnumerable<VersionEntryVM> Items =>
        IsCollapsible && !IsExpanded ? _all.Take(CollapsedLimit) : _all;

    public string ToggleText => IsExpanded ? "收起" : $"展开全部 {Total} 个";

    public VersionGroupVM(string title, IEnumerable<VersionEntryVM> items, bool isCollapsible = false)
    {
        Title = title;
        Total = items.Count();
        _all = items.ToList();
        IsCollapsible = isCollapsible && Total > CollapsedLimit;
    }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(ToggleText));
    }
}

/// <summary>版本行：下载按钮 / 已安装徽章 / 下载中迷你进度</summary>
public partial class VersionEntryVM : ObservableObject
{
    private readonly VersionInstaller? _installer;
    private readonly Action<VersionEntryVM>? _onInstalled;

    public string Id { get; }
    public string Type { get; }
    public string ReleaseDate { get; }
    public string? ManifestUrl { get; }

    [ObservableProperty]
    public partial bool Installed { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double DownloadProgressPercent { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    public bool ShowDownloadButton => !Installed && !IsDownloading;
    public bool ShowProgress => IsDownloading;
    public bool HasError => ErrorText.Length > 0;
    public string DownloadProgressText => $"{DownloadProgressPercent:0}%";

    public VersionEntryVM(string id, string type, bool installed, string releaseDate, string? manifestUrl,
        VersionInstaller? installer = null, Action<VersionEntryVM>? onInstalled = null)
    {
        Id = id;
        Type = type;
        Installed = installed;
        ReleaseDate = releaseDate;
        ManifestUrl = manifestUrl;
        _installer = installer;
        _onInstalled = onInstalled;
    }

    [RelayCommand]
    private async Task Download()
    {
        if (IsDownloading || Installed || _installer is null) return;
        IsDownloading = true;
        ErrorText = "";
        try
        {
            var version = await _installer.GetOrFetchVersionJsonAsync(Id, ManifestUrl, CancellationToken.None);

            var task = DownloadManager.Instance.Enqueue($"下载 {Id}", (p, ct) => _installer.InstallAsync(version, p, ct));
            void Sync(object? _, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(DownloadTask.ProgressPercent))
                    DownloadProgressPercent = task.ProgressPercent;
                if (e.PropertyName == nameof(DownloadTask.Error) && task.Error is { } err)
                    ErrorText = err;
            }
            task.PropertyChanged += Sync;
            try { await task.Completion; }
            finally { task.PropertyChanged -= Sync; }

            if (task.State == DownloadTaskState.Completed)
            {
                Installed = true;
                _onInstalled?.Invoke(this);
            }
            else if (task.Error is { } failed)
            {
                ErrorText = failed;
            }
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsDownloading = false;
            OnPropertyChanged(nameof(ShowDownloadButton));
            OnPropertyChanged(nameof(ShowProgress));
            OnPropertyChanged(nameof(HasError));
        }
    }
}
