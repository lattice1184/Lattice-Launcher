using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Download;
using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

/// <summary>
/// 资源下载面板（下载板块的一个 tab）：防抖搜索 + 实例过滤 + 卡片流 + 四态 + 分页。
/// 类型在构造时固定（下载页为每种类型建一个实例）；tab 切换由外层 DownloadViewModel 控制。
/// </summary>
public partial class EcosystemViewModel : ViewModelBase
{
    private readonly EcosystemService _eco = new();
    private readonly ProjectType _type;
    private CancellationTokenSource? _searchCts;
    private int _requestSeq;

    private const int PageSize = 20;

    public EcosystemViewModel(ProjectType type = ProjectType.Mod)
    {
        _type = type;
        SelectedSort = SortOptions[0];
        SelectedGameVersion = GameVersionOptions[0];
    }

    /// <summary>tab 显示名（MOD/整合包/材质包/光影包）</summary>
    public string TabName => _type switch
    {
        ProjectType.Modpack => "整合包",
        ProjectType.Resourcepack => "材质包",
        ProjectType.Shader => "光影包",
        _ => "MOD",
    };

    /// <summary>仅 MOD 类型显示加载器 chips（材质包/光影无加载器概念）</summary>
    public bool IsModType => _type == ProjectType.Mod;

    // ---------- 三级筛选选项 ----------

    /// <summary>加载器 chips（"全部"=null）</summary>
    public static IReadOnlyList<string> LoaderOptions { get; } = ["全部", "Fabric", "Forge", "NeoForge", "Quilt"];

    /// <summary>游戏版本下拉（"跟随实例"=null + 常用版本）——Display/Value 分离，避免字面字符串当过滤条件</summary>
    public static IReadOnlyList<GameVersionOption> GameVersionOptions { get; } =
    [
        new GameVersionOption("跟随实例", null),
        new GameVersionOption("1.21.6", "1.21.6"),
        new GameVersionOption("1.21.5", "1.21.5"),
        new GameVersionOption("1.21.4", "1.21.4"),
        new GameVersionOption("1.21.3", "1.21.3"),
        new GameVersionOption("1.21.1", "1.21.1"),
        new GameVersionOption("1.20.4", "1.20.4"),
        new GameVersionOption("1.20.1", "1.20.1"),
        new GameVersionOption("1.19.4", "1.19.4"),
        new GameVersionOption("1.18.2", "1.18.2"),
    ];

    public sealed record GameVersionOption(string Display, string? Value);

    /// <summary>排序选项（下载量/更新时间/关注/最新）</summary>
    public static IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new SortOption("相关度", EcosystemService.SortIndex.Relevance),
        new SortOption("下载量", EcosystemService.SortIndex.Downloads),
        new SortOption("最近更新", EcosystemService.SortIndex.Updated),
        new SortOption("关注数", EcosystemService.SortIndex.Follows),
        new SortOption("最新发布", EcosystemService.SortIndex.Newest),
    ];

    public sealed record SortOption(string Display, EcosystemService.SortIndex Index);

    /// <summary>功能分类（Modrinth categories，中文显示；"全部"=null）</summary>
    public static IReadOnlyList<CategoryOption> CategoryOptions { get; } =
    [
        new CategoryOption("全部", null),
        new CategoryOption("优化", "optimization"),
        new CategoryOption("辅助", "utility"),
        new CategoryOption("冒险", "adventure"),
        new CategoryOption("装饰", "decorations"),
        new CategoryOption("魔法", "magic"),
        new CategoryOption("世界生成", "worldgen"),
        new CategoryOption("科技", "technology"),
        new CategoryOption("存储", "storage"),
        new CategoryOption("装备", "equipment"),
        new CategoryOption("库", "library"),
        new CategoryOption("生物", "mobs"),
        new CategoryOption("红石", "redstone"),
    ];

    public sealed record CategoryOption(string Display, string? Key);

    /// <summary>加载器筛选（null=跟随实例猜测）</summary>
    [ObservableProperty]
    public partial string? SelectedLoader { get; set; }

    /// <summary>游戏版本筛选（选中"跟随实例"时 Value=null → 跟随实例解析）</summary>
    [ObservableProperty]
    public partial GameVersionOption? SelectedGameVersion { get; set; }

    /// <summary>功能分类筛选（null=全部）</summary>
    [ObservableProperty]
    public partial CategoryOption? SelectedCategory { get; set; }

    /// <summary>排序（默认相关度）</summary>
    [ObservableProperty]
    public partial SortOption SelectedSort { get; set; }

    /// <summary>只看收藏（星标项目；从 FavoritesService 拉取）</summary>
    [ObservableProperty]
    public partial bool FavoritesOnly { get; set; }

    partial void OnFavoritesOnlyChanged(bool value) => _ = RunSearchAsync(reset: true);

    [RelayCommand]
    private void ToggleFavorites() => FavoritesOnly = !FavoritesOnly;

    public ObservableCollection<ProjectCardVM> Cards { get; } = [];
    public ObservableCollection<VersionInstanceVM> Instances { get; } = [];

    [ObservableProperty]
    public partial VersionInstanceVM? SelectedInstance { get; set; }

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsError { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    // 分页状态（◀ 页码 ▶）
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

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial ProjectDetailViewModel? Detail { get; set; }

    [ObservableProperty]
    public partial bool IsDetailOpen { get; set; }

    partial void OnDetailChanged(ProjectDetailViewModel? value) => IsDetailOpen = value is not null;

    // 筛选变化立即搜索（不走防抖——Modrinth facets 服务器筛选快，延迟全在防抖；竞态 seq 丢弃旧响应）
    partial void OnSelectedLoaderChanged(string? value) => _ = RunSearchAsync(reset: true);
    partial void OnSelectedGameVersionChanged(GameVersionOption? value) => _ = RunSearchAsync(reset: true);

    /// <summary>切换目标实例 → 立即按新实例重新搜索（列表与实例保持一致）</summary>
    partial void OnSelectedInstanceChanged(VersionInstanceVM? value) => _ = RunSearchAsync(reset: true);
    partial void OnSelectedCategoryChanged(CategoryOption? value) => _ = RunSearchAsync(reset: true);
    partial void OnSelectedSortChanged(SortOption value) => _ = RunSearchAsync(reset: true);

    /// <summary>加载器 chips 选择（"全部"=null；值转小写——Modrinth facets 要求 fabric/forge/neoforge/quilt）</summary>
    [RelayCommand]
    private void SelectLoader(string loader)
        => SelectedLoader = loader == "全部" ? null : loader.ToLowerInvariant();

    /// <summary>初始化：扫描已装实例（跨扫描源补漏：加载器版本不在 Mojang manifest）并触发首搜</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var svc = new VersionManifestService();
            await svc.RefreshAsync();
            Instances.Clear();
            foreach (var e in svc.Entries.Where(e => e.Installed))
                Instances.Add(new VersionInstanceVM(e.Id));
            // 目录补漏：fabric/forge/neoforge/quilt 等不在 manifest 的已装版本
            foreach (var (dir, _) in Launcher.Core.Utils.GameDirectory.ScanSourceDirs())
            {
                var versionsDir = Path.Combine(dir, "versions");
                if (!Directory.Exists(versionsDir)) continue;
                foreach (var d in Directory.EnumerateDirectories(versionsDir))
                {
                    var id = Path.GetFileName(d);
                    if (Instances.Any(i => i.Name.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                    if (File.Exists(Path.Combine(d, $"{id}.json")))
                        Instances.Add(new VersionInstanceVM(id));
                }
            }
        }
        catch { /* 实例扫描失败不阻塞搜索 */ }

        if (Instances.Count > 0) SelectedInstance = Instances[0];
        await RunSearchAsync(reset: true);
    }

    partial void OnQueryChanged(string value) => DebouncedSearch();

    /// <summary>防抖搜索（150ms，取消旧请求——仅搜索框需要防抖）</summary>
    private async void DebouncedSearch()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(150, cts.Token);
            await RunSearchAsync(reset: true, cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunSearchAsync(bool reset, CancellationToken ct = default)
    {
        var seq = ++_requestSeq;
        if (reset) CurrentPage = 0; // 搜索/筛选变化回第 1 页
        IsLoading = true;
        IsError = false;
        IsEmpty = false;
        try
        {
            if (FavoritesOnly)
            {
                await LoadFavoritesAsync(seq, ct);
                return;
            }
            var instance = SelectedInstance;
            // 三级筛选：显式选择优先，否则跟随实例（加载器猜测/版本解析）
            var loader = SelectedLoader
                ?? (instance is not null ? EcosystemService.GuessLoader(instance.Name) : null);
            var gameVersion = SelectedGameVersion?.Value
                ?? (instance is not null && EcosystemService.TryParseGameVersion(instance.Name, out var gv) ? gv : null);
            var category = SelectedCategory?.Key;

            var resp = await _eco.SearchAsync(_type, Query, gameVersion, loader, category,
                index: SelectedSort?.Index ?? EcosystemService.SortIndex.Relevance,
                limit: PageSize, offset: CurrentPage * PageSize, ct);
            if (seq != _requestSeq) return; // 竞态：旧响应直接丢弃

            Cards.Clear(); // 服务器分页：每次重建当前页
            var hits = resp?.Hits ?? [];
            foreach (var h in hits) Cards.Add(new ProjectCardVM(h));
            var total = resp?.TotalHits ?? 0;
            TotalPages = Math.Max(1, (total + PageSize - 1) / PageSize);
            HasPrev = CurrentPage > 0;
            HasNext = CurrentPage < TotalPages - 1;
            PageText = $"{CurrentPage + 1}/{TotalPages}";
            IsEmpty = Cards.Count == 0;
            // 状态提示：筛选版本时列表天然只含适配项（Modrinth facets 语义）
            Status = resp is null ? "无响应"
                : gameVersion is not null ? $"共 {total} 个结果 · 已按 {gameVersion} 过滤"
                : $"共 {total} 个结果";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (seq != _requestSeq) return;
            IsError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (seq == _requestSeq) IsLoading = false;
        }
    }

    // 无参命令：避免 RelayCommand<bool> 与 XAML string CommandParameter 的类型不匹配崩溃
    [RelayCommand]
    private Task Search() => RunSearchAsync(reset: true);

    [RelayCommand]
    private void PrevPage()
    {
        if (CurrentPage <= 0) return;
        CurrentPage--;
        _ = RunSearchAsync(reset: false);
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage >= TotalPages - 1) return;
        CurrentPage++;
        _ = RunSearchAsync(reset: false);
    }

    /// <summary>项目类型匹配（大小写不敏感；MOD 匹配全部非特殊类型）</summary>
    private bool TypeMatches(string? projectType)
        => _type == ProjectType.Mod
            ? projectType is not ("modpack" or "resourcepack" or "shader")
            : string.Equals(projectType, _type.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>收藏模式：逐项目拉详情组装卡片（收藏数小，直拉可接受）</summary>
    private async Task LoadFavoritesAsync(int seq, CancellationToken ct)
    {
        var ids = FavoritesService.All;
        Cards.Clear();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var detail = await _eco.GetProjectAsync(id, ct);
                if (detail is not null && TypeMatches(detail.ProjectType))
                    Cards.Add(new ProjectCardVM(detail));
            }
            catch { /* 单个拉取失败跳过 */ }
        }
        if (seq != _requestSeq) return;
        TotalPages = 1;
        HasPrev = false;
        HasNext = false;
        PageText = "1/1";
        IsEmpty = Cards.Count == 0;
        Status = $"收藏 {Cards.Count} 个项目";
        if (seq == _requestSeq) IsLoading = false;
    }

    /// <summary>卡片一键安装：匹配版本 → 依赖确认（全部/仅主文件）→ 全局下载中心执行 → Toast</summary>
    [RelayCommand]
    private async Task InstallCard(ProjectCardVM card)
    {
        var instance = SelectedInstance;
        var gameVersion = instance is not null && EcosystemService.TryParseGameVersion(instance.Name, out var gv) ? gv : null;
        var loader = instance is not null ? EcosystemService.GuessLoader(instance.Name) : null;
        try
        {
            var version = await _eco.FindBestVersionAsync(card.Id, gameVersion, loader, CancellationToken.None);
            if (version is null)
            {
                NotificationService.Error($"{card.Title} 没有适配当前实例的版本");
                return;
            }

            // 依赖解析内部同步等网络（EcosystemDependencyAdapter .GetResult()）——必须离线 UI 线程，否则永久死锁
            var deps = await Task.Run(() =>
                _eco.ResolveDependencyNamesAsync(version, gameVersion, loader, CancellationToken.None));
            var includeDeps = true;
            if (deps.Count > 0 && DialogService.MainWindow() is { } owner)
            {
                var list = string.Join("、", deps.Take(6)) + (deps.Count > 6 ? "…" : "");
                includeDeps = await DialogService.Confirm(owner,
                    $"将安装 {deps.Count} 个前置：{list}", $"安装 {card.Title}", "全部安装", "仅主文件");
            }

            if (instance is null)
            {
                NotificationService.Error("请先在顶部选择目标实例");
                return;
            }
            var instanceName = instance.Name;
            var task = DownloadManager.Instance.Enqueue($"安装 {card.Title}", (p, ct) =>
                includeDeps
                    ? _eco.InstallWithDependenciesAsync(card.Id, version, instanceName, card.Type,
                        gameVersion, loader, dp => p(dp), ct)
                    : InstallMainOnlyAsync(card.Id, version, instanceName, card.Type, p, ct));
            await task.Completion;
            if (task.State == DownloadTaskState.Completed)
                NotificationService.Success($"{card.Title} 安装完成");
            else if (task.Error is { } err)
                NotificationService.Error(err);
        }
        catch (Exception ex)
        {
            NotificationService.Error($"安装失败: {ex.Message}");
        }
    }

    /// <summary>仅安装主文件（依赖可选跳过路径）</summary>
    private Task InstallMainOnlyAsync(string projectId, ModrinthVersion version, string instanceName,
        ProjectType type, DownloadProgressHandler progress, CancellationToken ct)
        => _eco.InstallAsync(projectId, version, instanceName, type, dp => progress(dp), ct);

    [RelayCommand]
    private void OpenDetail(ProjectCardVM card) =>
        Detail = new ProjectDetailViewModel(_eco, card, SelectedInstance, () => Detail = null);

    [RelayCommand]
    private void CloseDetail() => Detail = null;
}
