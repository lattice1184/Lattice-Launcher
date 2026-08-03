using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Download;
using Launcher.Core.Services;
using Launcher.Core.Utils;

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

    private int _loaded;

    /// <summary>幂等加载（顶级导航多次进入只拉一次清单；失败不置位——下次进入可重试）</summary>
    public async Task EnsureLoadedAsync()
    {
        if (Volatile.Read(ref _loaded) == 1) return;
        try
        {
            await LoadAsync();
            Volatile.Write(ref _loaded, 1);
        }
        catch
        {
            // LoadAsync 内部已 catch 并写 Status；失败时 _loaded 保持 0，下次导航重试
        }
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

    /// <summary>整合包 zip 选择（View 层 FilePicker 回调）</summary>
    public Func<Task<string?>>? PickModpackFile { get; set; }

    /// <summary>导入整合包：选 zip → 确认 → 解压为隔离实例 → Toast + 重扫</summary>
    [RelayCommand]
    private async Task ImportModpack()
    {
        if (PickModpackFile is null) return;
        var file = await PickModpackFile();
        if (file is null) return;

        var info = ModpackImporter.Parse(file, out var reason);
        if (info is null)
        {
            NotificationService.Error(reason ?? "无法解析整合包");
            return;
        }
        var owner = DialogService.MainWindow();
        if (owner is null || !await DialogService.Confirm(owner,
                $"导入整合包「{info.VersionId}」？ MC {info.McVersion} · {info.FileCount} 个文件，将解压到版本目录。",
                "导入整合包", "导入", "取消"))
        {
            return;
        }

        try
        {
            var dir = GameDirectory.InstallDir();
            await Task.Run(() => ModpackImporter.Import(file, dir, CancellationToken.None));
            NotificationService.Success($"已导入 {info.VersionId}");
            OnInstalled(info.VersionId);
        }
        catch (Exception ex)
        {
            NotificationService.Error($"导入失败: {ex.Message}");
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

/// <summary>左栏：分类导航 + 搜索 + 版本列表（分页 10/页，左右箭头翻页）</summary>
public partial class VersionSidebarViewModel : ObservableObject
{
    private const int PageSize = 10;

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

    // 分页状态
    [ObservableProperty]
    public partial int CurrentPage { get; set; }

    [ObservableProperty]
    public partial int TotalPages { get; set; } = 1;

    [ObservableProperty]
    public partial bool HasPrev { get; set; }

    [ObservableProperty]
    public partial bool HasNext { get; set; }

    [ObservableProperty]
    public partial string PageText { get; set; } = "1/1";

    /// <summary>列表区透明度（分类/翻页切换时 0→1 淡入过渡，去硬切感）</summary>
    [ObservableProperty]
    public partial double ListOpacity { get; set; } = 1;

    public event Action<VersionListItemVM>? SelectionChanged;

    public void SetAllEntries(List<VersionManifestService.GameVersionEntry> all)
    {
        _all = all;
        _itemsById.Clear();
        foreach (var e in all)
            _itemsById[e.Id] = new VersionListItemVM(e.Id, e.Type, e.Installed,
                e.ReleaseTime.ToString("yyyy-MM-dd"), e.ManifestUrl, e.GameDirectory);
    }

    [RelayCommand]
    private void SelectCategory(VersionCategoryItemVM category) => SelectedCategory = category;

    [RelayCommand]
    private void PrevPage()
    {
        if (CurrentPage <= 0) return;
        SelectedItem = null;
        CurrentPage--;
        RebuildItems();
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage >= TotalPages - 1) return;
        SelectedItem = null;
        CurrentPage++;
        RebuildItems();
    }

    partial void OnSelectedCategoryChanged(VersionCategoryItemVM? value)
    {
        SelectedItem = null;
        CurrentPage = 0;
        RebuildItems();
    }

    partial void OnSearchTextChanged(string value)
    {
        CurrentPage = 0;
        RebuildItems();
    }

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
            // 搜索跨分类过滤（英文 id 子串 + 中文关键词）
            source = _all.Where(e => Matches(e, SearchText));
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

        // 分页：每页 10 条，页码重置到当前页（分类/搜索变化时回第 1 页由调用方置 0）
        var all = source.ToList();
        TotalPages = Math.Max(1, (all.Count + PageSize - 1) / PageSize);
        if (CurrentPage >= TotalPages) CurrentPage = TotalPages - 1;
        HasPrev = CurrentPage > 0;
        HasNext = CurrentPage < TotalPages - 1;
        PageText = $"{CurrentPage + 1}/{TotalPages}";
        foreach (var e in all.Skip(CurrentPage * PageSize).Take(PageSize))
            Items.Add(_itemsById[e.Id]);

        // 列表内容切换：先透明再淡入（DoubleTransition 平滑过渡）
        ListOpacity = 0;
        Dispatcher.UIThread.Post(() => ListOpacity = 1);
    }

    /// <summary>版本匹配：英文 id 子串或中文关键词（正式/稳定→release，快照→snapshot，远古→old_*，愚人→愚人节）</summary>
    private static bool Matches(VersionManifestService.GameVersionEntry e, string kw)
    {
        if (e.Id.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
        return kw switch
        {
            "正式" or "稳定" => e.Type == "release",
            "快照" => e.Type == "snapshot",
            "远古" => e.Type is "old_alpha" or "old_beta",
            "愚人" => VersionClassifier.IsAprilFools(e),
            _ => false,
        };
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

    /// <summary>版本所在游戏目录（安装/管理落点；空 = 未安装）</summary>
    public string GameDirectory { get; }

    [ObservableProperty]
    public partial bool Installed { get; set; }

    public VersionListItemVM(string id, string type, bool installed, string releaseDate, string? manifestUrl,
        string gameDirectory = "")
    {
        Id = id;
        Type = type;
        Installed = installed;
        ReleaseDate = releaseDate;
        ManifestUrl = manifestUrl;
        GameDirectory = gameDirectory;
    }
}

/// <summary>右栏详情：下载（组任务）/ 进度 / 体积 / 加载器安装</summary>
public partial class VersionDetailViewModel : ViewModelBase
{
    private readonly VersionInstaller _installer;
    private readonly Action<string> _onInstalled;
    private int _sizeGeneration;

    /// <summary>当前选中版本所在目录（Repair 时用版本实际目录，不默认 InstallDir）</summary>
    private string _currentDir = "";

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

    /// <summary>版本管理（已安装时创建：删除/备份/导出/MOD/存档）</summary>
    [ObservableProperty]
    public partial VersionManageViewModel? Manage { get; set; }

    /// <summary>详情面板滑入偏移（选中切换时 24→0，去硬切感）</summary>
    [ObservableProperty]
    public partial double SlideX { get; set; }

    /// <summary>详情面板透明度（选中切换时 0→1 淡入）</summary>
    [ObservableProperty]
    public partial double DetailOpacity { get; set; } = 1;

    public string? ManifestUrl { get; private set; }
    public bool ShowDownloadButton => !Installed && !IsDownloading;

    /// <summary>已安装版本显示"重新下载"（损坏修复）</summary>
    public bool ShowRepairButton => Installed && !IsDownloading;
    public bool ShowProgress => IsDownloading;
    public bool HasError => ErrorText.Length > 0;
    public string DownloadProgressText => $"{DownloadProgressPercent:0}%";

    /// <summary>下载中"查看下载进度"跳转（切到下载记录 tab）</summary>
    [RelayCommand]
    private void GoToDownloadQueue() => MainViewModel.Current?.NavigateToDownloadQueue();

    public VersionDetailViewModel(VersionInstaller installer, Action<string> onInstalled)
    {
        _installer = installer;
        _onInstalled = onInstalled;
    }

    /// <summary>选中左栏行 → 填充详情 + 懒加载体积（同版本重复选中不重建；内容切换滑入淡入）</summary>
    public void Select(VersionListItemVM item)
    {
        if (HasSelection && Id == item.Id) return; // 面板状态（加载器选择等）不因重复点击丢失

        Id = item.Id;
        Type = item.Type;
        ReleaseDate = item.ReleaseDate;
        Installed = item.Installed;
        ManifestUrl = item.ManifestUrl;
        ErrorText = "";
        DownloadProgressPercent = 0;
        SizeText = "预估体积：计算中…";
        HasSelection = true;
        var dir = item.GameDirectory.Length > 0 ? item.GameDirectory : GameDirectory.Detect();
        _currentDir = dir;
        Loader = new LoaderPickerViewModel(item.Id, dir, () => _onInstalled(item.Id));
        Manage = item.Installed
            ? new VersionManageViewModel(dir, item.Id, OnVersionDeleted)
            : null;

        // 内容切换过渡：先偏移透明 → UI 线程复位（DoubleTransition 平滑）
        SlideX = 24;
        DetailOpacity = 0;
        Dispatcher.UIThread.Post(() =>
        {
            SlideX = 0;
            DetailOpacity = 1;
        });
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

    /// <summary>版本删除成功：重扫 + 关闭详情</summary>
    private void OnVersionDeleted()
    {
        _onInstalled(Id);
        HasSelection = false;
        Manage = null;
        Loader = null;
    }

    [RelayCommand]
    private async Task Download() => await DownloadCoreAsync(repair: false);

    /// <summary>重新下载/修复：确认后重装缺失或损坏文件（幂等跳过已有文件）</summary>
    [RelayCommand]
    private async Task Repair()
    {
        if (IsDownloading) return;
        var owner = DialogService.MainWindow();
        if (owner is null || !await DialogService.Confirm(owner,
                $"将重新下载 {Id} 的缺失或损坏文件（已有文件自动跳过）。继续？",
                "重新下载", "重新下载", "取消"))
        {
            return;
        }
        await DownloadCoreAsync(repair: true);
    }

    private async Task DownloadCoreAsync(bool repair)
    {
        if (IsDownloading) return;
        if (!repair && Installed) return;
        // 快照：下载期间用户可能切到其他版本——完成回调/Toast 必须用发起时的版本
        var targetId = Id;
        var targetUrl = ManifestUrl;
        IsDownloading = true;
        ErrorText = "";
        DownloadProgressPercent = 0;
        try
        {
            // 修复（已装版本）用版本所在目录；首次下载用默认安装目录
            var installer = repair && _currentDir.Length > 0
                ? new VersionInstaller(gameDirectory: _currentDir)
                : _installer;
            var version = await installer.GetOrFetchVersionJsonAsync(targetId, targetUrl, CancellationToken.None);
            var task = DownloadManager.Instance.EnqueueGroup($"下载 {targetId}", (ctx, ct) =>
                installer.InstallAsync(version, ctx, ct));

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
                if (Id == targetId) Installed = true; // 面板仍显示该版本才点亮（切走后由重扫负责）
                _onInstalled(targetId);
                NotificationService.Success(repair ? $"{targetId} 修复完成" : $"{targetId} 安装完成");
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
            OnPropertyChanged(nameof(ShowRepairButton));
            OnPropertyChanged(nameof(ShowProgress));
            OnPropertyChanged(nameof(HasError));
        }
    }
}
