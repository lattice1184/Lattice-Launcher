using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Download;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

/// <summary>
/// 版本管理（PCL2 式分栏）：左侧分类导航 + 搜索，右侧版本详情（下载/加载器/体积）。
/// 904 个版本不再一页倒出——分类过滤 + ListBox 虚拟化。
/// </summary>
public partial class VersionBrowseViewModel : ViewModelBase
{
    private readonly VersionManifestService _svc;
    private readonly VersionInstaller _installer;

    public VersionSidebarViewModel Sidebar { get; }
    public VersionDetailViewModel Detail { get; }

    [ObservableProperty]
    public partial string Status { get; set; } = "加载中…";

    public VersionBrowseViewModel()
    {
        _svc = new VersionManifestService();
        _installer = new VersionInstaller();
        Sidebar = new VersionSidebarViewModel();
        Detail = new VersionDetailViewModel(_installer, OnInstalled);
        Sidebar.SelectionChanged += item => Detail.Select(item);
    }

    public async Task LoadAsync()
    {
        try
        {
            await _svc.RefreshAsync();
            var all = _svc.Entries.ToList();
            var releases = all.Where(e => e.Type == "release" && !VersionClassifier.IsAprilFools(e)).ToList();
            var snapshots = all.Where(e => e.Type == "snapshot" && !VersionClassifier.IsAprilFools(e)).ToList();
            var ancient = all.Where(e => e.Type is "old_alpha" or "old_beta").ToList();
            var april = all.Where(VersionClassifier.IsAprilFools).ToList();

            Sidebar.Categories.Clear();
            Sidebar.Categories.Add(new VersionCategoryItemVM("最新正式版", VersionCategory.LatestRelease,
                Math.Min(VersionClassifier.LatestReleaseCount, releases.Count), "最近 5 个稳定版本"));
            Sidebar.Categories.Add(new VersionCategoryItemVM("全部正式版", VersionCategory.AllReleases, releases.Count, "所有稳定版本"));
            Sidebar.Categories.Add(new VersionCategoryItemVM("快照", VersionCategory.Snapshots, snapshots.Count, "开发预览版"));
            Sidebar.Categories.Add(new VersionCategoryItemVM("远古", VersionCategory.Ancient, ancient.Count, "Alpha / Beta 时代"));
            Sidebar.Categories.Add(new VersionCategoryItemVM("愚人节", VersionCategory.AprilFools, april.Count, "4 月 1 日特别版"));

            Sidebar.SetAllEntries(all);
            Sidebar.SelectedCategory = Sidebar.Categories[0];
            Status = $"共 {all.Count} 个版本 · 左侧分类浏览";
        }
        catch (Exception ex)
        {
            Status = $"加载失败: {ex.Message}";
        }
    }

    /// <summary>安装完成：重扫磁盘并点亮已安装状态</summary>
    private void OnInstalled(string versionId)
    {
        _svc.RescanInstalled();
        var installedSet = new HashSet<string>(
            _svc.Entries.Where(e => e.Installed).Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
        Sidebar.RefreshInstalled(installedSet);
        Detail.RefreshInstalled(installedSet);
    }
}

/// <summary>左侧分类项（副标题解释分类含义）</summary>
public sealed record VersionCategoryItemVM(string Title, VersionCategory Kind, int Count, string Subtitle);

/// <summary>左栏：分类导航 + 搜索 + 版本列表（虚拟化）</summary>
public partial class VersionSidebarViewModel : ObservableObject
{
    private List<VersionManifestService.GameVersionEntry> _all = [];
    private readonly Dictionary<string, VersionListItemVM> _itemsById = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<VersionCategoryItemVM> Categories { get; } = [];
    public ObservableCollection<VersionListItemVM> Items { get; } = [];

    [ObservableProperty]
    public partial VersionCategoryItemVM? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial VersionListItemVM? SelectedItem { get; set; }

    public event Action<VersionListItemVM>? SelectionChanged;

    public void SetAllEntries(List<VersionManifestService.GameVersionEntry> all)
    {
        _all = all;
        _itemsById.Clear();
        foreach (var e in all)
            _itemsById[e.Id] = new VersionListItemVM(e.Id, e.Type, e.Installed,
                e.ReleaseTime.ToString("yyyy-MM-dd"), e.ManifestUrl);
    }

    [RelayCommand]
    private void SelectCategory(VersionCategoryItemVM category) => SelectedCategory = category;

    partial void OnSelectedCategoryChanged(VersionCategoryItemVM? value)
    {
        SelectedItem = null;
        RebuildItems();
    }

    partial void OnSearchTextChanged(string value) => RebuildItems();

    partial void OnSelectedItemChanged(VersionListItemVM? value)
    {
        if (value is not null) SelectionChanged?.Invoke(value);
    }

    private void RebuildItems()
    {
        Items.Clear();
        IEnumerable<VersionManifestService.GameVersionEntry> source;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            // 搜索跨分类过滤（904 条字符串过滤足够快）
            source = _all.Where(e => e.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            source = SelectedCategory?.Kind switch
            {
                VersionCategory.LatestRelease => _all.Where(e => e.Type == "release" && !VersionClassifier.IsAprilFools(e))
                                                    .Take(VersionClassifier.LatestReleaseCount),
                VersionCategory.AllReleases => _all.Where(e => e.Type == "release" && !VersionClassifier.IsAprilFools(e)),
                VersionCategory.Snapshots => _all.Where(e => e.Type == "snapshot" && !VersionClassifier.IsAprilFools(e)),
                VersionCategory.Ancient => _all.Where(e => e.Type is "old_alpha" or "old_beta"),
                VersionCategory.AprilFools => _all.Where(VersionClassifier.IsAprilFools),
                _ => [],
            };
        }
        foreach (var e in source) Items.Add(_itemsById[e.Id]);
    }

    /// <summary>安装完成重扫后点亮所有行</summary>
    public void RefreshInstalled(HashSet<string> installedSet)
    {
        foreach (var item in _itemsById.Values)
            item.Installed = installedSet.Contains(item.Id);
    }
}

/// <summary>左栏行（轻量，仅展示 + 选中）</summary>
public partial class VersionListItemVM : ObservableObject
{
    public string Id { get; }
    public string Type { get; }
    public string ReleaseDate { get; }
    public string? ManifestUrl { get; }

    [ObservableProperty]
    public partial bool Installed { get; set; }

    public VersionListItemVM(string id, string type, bool installed, string releaseDate, string? manifestUrl)
    {
        Id = id;
        Type = type;
        Installed = installed;
        ReleaseDate = releaseDate;
        ManifestUrl = manifestUrl;
    }
}

/// <summary>右栏详情：下载（组任务）/ 进度 / 体积 / 加载器安装</summary>
public partial class VersionDetailViewModel : ViewModelBase
{
    private readonly VersionInstaller _installer;
    private readonly Action<string> _onInstalled;
    private int _sizeGeneration;

    [ObservableProperty]
    public partial string Id { get; set; } = "";

    [ObservableProperty]
    public partial string Type { get; set; } = "";

    [ObservableProperty]
    public partial string ReleaseDate { get; set; } = "";

    [ObservableProperty]
    public partial string SizeText { get; set; } = "";

    [ObservableProperty]
    public partial bool Installed { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double DownloadProgressPercent { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial LoaderPickerViewModel? Loader { get; set; }

    public string? ManifestUrl { get; private set; }
    public bool ShowDownloadButton => !Installed && !IsDownloading;
    public bool ShowProgress => IsDownloading;
    public bool HasError => ErrorText.Length > 0;
    public string DownloadProgressText => $"{DownloadProgressPercent:0}%";

    public VersionDetailViewModel(VersionInstaller installer, Action<string> onInstalled)
    {
        _installer = installer;
        _onInstalled = onInstalled;
    }

    /// <summary>选中左栏行 → 填充详情 + 懒加载体积</summary>
    public void Select(VersionListItemVM item)
    {
        Id = item.Id;
        Type = item.Type;
        ReleaseDate = item.ReleaseDate;
        Installed = item.Installed;
        ManifestUrl = item.ManifestUrl;
        ErrorText = "";
        DownloadProgressPercent = 0;
        SizeText = "预估体积：计算中…";
        HasSelection = true;
        Loader = new LoaderPickerViewModel(item.Id, () => _onInstalled(item.Id));
        _ = LoadSizeAsync(item);
    }

    /// <summary>懒取 version.json 估算体积（generation counter 防快速切换的过期结果）</summary>
    private async Task LoadSizeAsync(VersionListItemVM item)
    {
        var gen = ++_sizeGeneration;
        try
        {
            var version = await _installer.GetOrFetchVersionJsonAsync(item.Id, item.ManifestUrl, CancellationToken.None);
            if (gen != _sizeGeneration) return;
            long total = version.Downloads?.Client?.Size ?? 0;
            foreach (var lib in version.Libraries ?? [])
            {
                total += lib.Downloads?.Artifact?.Size ?? 0;
                if (lib.Downloads?.Classifiers is { } c) total += c.Values.Sum(x => x.Size ?? 0);
            }
            total += version.AssetIndex?.TotalSize ?? 0;
            total += version.Logging?.Client?.File?.Size ?? 0;
            SizeText = total > 0 ? $"预估体积：{FormatMb(total)}" : "";
        }
        catch
        {
            if (gen == _sizeGeneration) SizeText = "";
        }
    }

    private static string FormatMb(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024.0 / 1024:0.0} MB" : $"{bytes / 1024.0:0} KB";

    public void RefreshInstalled(HashSet<string> installedSet)
    {
        if (HasSelection && installedSet.Contains(Id)) Installed = true;
    }

    [RelayCommand]
    private async Task Download()
    {
        if (IsDownloading || Installed) return;
        IsDownloading = true;
        ErrorText = "";
        DownloadProgressPercent = 0;
        try
        {
            var version = await _installer.GetOrFetchVersionJsonAsync(Id, ManifestUrl, CancellationToken.None);
            var task = DownloadManager.Instance.EnqueueGroup($"下载 {Id}", (ctx, ct) =>
                _installer.InstallAsync(version, ctx, ct));

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
                _onInstalled(Id);
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
