using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Download;
using Launcher.Core.Ecosystem;
using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;
using PCL.Core.Minecraft.ResourceProject.Curseforge;

namespace Launcher.App.ViewModels;

/// <summary>
/// 资源下载面板（下载板块的一个 tab）：防抖搜索 + 实例过滤 + 卡片流 + 四态 + 分页。
/// 类型在构造时固定（下载页为每种类型建一个实例）；tab 切换由外层 DownloadViewModel 控制。
/// 来源筛选：全部 = Modrinth + CurseForge 双源并行合并。
/// </summary>
public partial class EcosystemViewModel : ViewModelBase
{
    private readonly EcosystemService _eco = new();
    private readonly CurseForgeService _cf = new();
    private readonly ProjectType _type;
    private CancellationTokenSource? _searchCts;
    private int _requestSeq;

    private const int PageSize = 20;

    public EcosystemViewModel(ProjectType type = ProjectType.Mod)
    {
        _type = type;
        SelectedSort = SortOptions[0];
        SelectedGameVersion = GameVersionOptions[0];
        BuildSourceOptions();
        SelectedSource = SourceOptions[0];
        // 全局版本绑定：主页切换版本 → 本页实例下拉跟随（AF1）
        if (MainViewModel.Current is { } main)
            main.PropertyChanged += OnMainPropertyChanged;
    }

    private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CurrentVersion)) return;
        if (MainViewModel.Current?.CurrentVersion is not { } cur) return;
        var hit = Instances.FirstOrDefault(i => i.Name.Equals(cur.Name, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) SelectedInstance = hit;
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

    /// <summary>来源筛选（全部 = 双源并行合并）。CurseForge 未配置 key 时选项带标记（视觉置灰提示）。</summary>
    public IReadOnlyList<SourceOption> SourceOptions { get; private set; } = [];

    public sealed record SourceOption(string Display, string? Key);

    private void BuildSourceOptions() =>
        SourceOptions =
        [
            new SourceOption("全部", null),
            new SourceOption("Modrinth", "modrinth"),
            new SourceOption(_cf.IsEnabled ? "CurseForge" : "CurseForge（未配置 Key）", "curseforge"),
        ];

    [ObservableProperty]
    public partial SourceOption? SelectedSource { get; set; }

    partial void OnSelectedSourceChanged(SourceOption? value) => _ = RunSearchAsync(reset: true);

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
            var all = new List<VersionInstanceVM>();
            var svc = new VersionManifestService();
            await svc.RefreshAsync();
            foreach (var e in svc.Entries.Where(e => e.Installed))
                all.Add(new VersionInstanceVM(e.Id, e.GameDirectory.Length > 0
                    ? Launcher.Core.Utils.GameDirectory.SourceLabel(Launcher.Core.Utils.GameDirectory.SourceOf(e.GameDirectory))
                    : "", e.GameDirectory,
                    Launcher.Core.Launch.LoaderDetector.Detect(e.GameDirectory, e.Id) ?? ""));
            // 目录补漏：fabric/forge/neoforge/quilt 等不在 manifest 的已装版本（带来源目录——MOD 落点关键）
            foreach (var (dir, _) in Launcher.Core.Utils.GameDirectory.ScanSourceDirs())
            {
                var versionsDir = Path.Combine(dir, "versions");
                if (!Directory.Exists(versionsDir)) continue;
                foreach (var d in Directory.EnumerateDirectories(versionsDir))
                {
                    var id = Path.GetFileName(d);
                    if (all.Any(i => i.Name.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                    // AL29：已安装 = json+jar（残件版本不作 mod 安装目标）
                    if (VersionManifestService.IsInstalled(dir, id))
                        all.Add(new VersionInstanceVM(id, Launcher.Core.Utils.GameDirectory.SourceLabel(
                            Launcher.Core.Utils.GameDirectory.SourceOf(dir)), dir,
                            Launcher.Core.Launch.LoaderDetector.Detect(dir, id) ?? ""));
                }
            }
            // 分批填充：前 5 立即，剩余每批 8 静默补全（大列表不卡，复用 LoaderChoiceDialog 模式）
            foreach (var v in all.Take(5)) Instances.Add(v);
            var rest = all.Skip(5).ToList();
            for (var i = 0; i < rest.Count; i += 8)
            {
                await Task.Delay(25);
                foreach (var v in rest.Skip(i).Take(8)) Instances.Add(v);
            }
        }
        catch { /* 实例扫描失败不阻塞搜索 */ }

        // 全局版本绑定：主页当前版本优先选中（AF1），否则第一个
        if (Instances.Count > 0)
            SelectedInstance = MainViewModel.Current?.CurrentVersion is { } cur
                && Instances.FirstOrDefault(i => i.Name.Equals(cur.Name, StringComparison.OrdinalIgnoreCase)) is { } hit
                ? hit
                : Instances[0];
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
            // 三级筛选：显式选择优先，否则跟随实例（真实加载器徽章优先——AG1，名字猜测兜底）
            var loader = SelectedLoader
                ?? (instance is not null && instance.LoaderBadge.Length > 0 ? instance.LoaderBadge
                    : instance is not null ? EcosystemService.GuessLoader(instance.Name) : null);
            var gameVersion = SelectedGameVersion?.Value
                ?? (instance is not null && EcosystemService.TryParseGameVersion(instance.Name, out var gv) ? gv : null);
            var category = SelectedCategory?.Key;

            var source = SelectedSource?.Key;
            if (source == "curseforge")
                await RunCfSearchAsync(seq, loader, gameVersion, ct);
            else if (source == "modrinth")
                await RunMrSearchAsync(seq, loader, gameVersion, category, ct);
            else
                await RunBothSearchAsync(seq, loader, gameVersion, category, ct);
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

    private async Task RunMrSearchAsync(int seq, string? loader, string? gameVersion, string? category, CancellationToken ct)
    {
        var resp = await _eco.SearchAsync(_type, Query, gameVersion, loader, category,
            index: SelectedSort?.Index ?? EcosystemService.SortIndex.Relevance,
            limit: PageSize, offset: CurrentPage * PageSize, ct);
        if (seq != _requestSeq) return; // 竞态：旧响应直接丢弃
        Cards.Clear(); // 服务器分页：每次重建当前页
        AddCards(resp?.Hits ?? [], h => h.Title, h => h.Description, h => new ProjectCardVM(h));
        FinishPage(seq, resp?.TotalHits ?? 0, gameVersion, resp is null ? "无响应" : null);
    }

    private async Task RunCfSearchAsync(int seq, string? loader, string? gameVersion, CancellationToken ct)
    {
        if (!_cf.IsEnabled)
        {
            if (seq != _requestSeq) return;
            Cards.Clear();
            FinishPage(seq, 0, gameVersion, "未配置 CurseForge API Key（设置 → CurseForge API Key）");
            return;
        }
        var sort = CfSortOf(SelectedSort?.Index);
        var page = await _cf.SearchAsync(_type, Query, gameVersion, sort, PageSize, CurrentPage, ct);
        if (seq != _requestSeq) return;
        Cards.Clear();
        AddCards(page?.Projects ?? [], p => p.name, p => p.summary, p => new ProjectCardVM(p));
        FinishPage(seq, page?.TotalCount ?? 0, gameVersion, page is null ? "无响应" : null);
    }

    private async Task RunBothSearchAsync(int seq, string? loader, string? gameVersion, string? category, CancellationToken ct)
    {
        var sort = CfSortOf(SelectedSort?.Index);
        // 双源并行发起、独立捕获：单源失败（超时/网络/限流）只降级该源，另一源照常显示
        var mrTask = _eco.SearchAsync(_type, Query, gameVersion, loader, category,
            index: SelectedSort?.Index ?? EcosystemService.SortIndex.Relevance,
            limit: PageSize, offset: CurrentPage * PageSize, ct);
        var cfTask = _cf.IsEnabled
            ? _cf.SearchAsync(_type, Query, gameVersion, sort, PageSize, CurrentPage, ct)
            : Task.FromResult<CurseForgeSearchPage?>(null);
        string? mrErr = null, cfErr = null;
        var mr = await TrySearchAsync(mrTask, ex => mrErr = ex.Message);
        var cf = await TrySearchAsync(cfTask, ex => cfErr = ex.Message);
        if (seq != _requestSeq) return;
        Cards.Clear();
        AddCards(mr?.Hits ?? [], h => h.Title, h => h.Description, h => new ProjectCardVM(h));
        AddCards(cf?.Projects ?? [], p => p.name, p => p.summary, p => new ProjectCardVM(p));
        var total = (mr?.TotalHits ?? 0) + (cf?.TotalCount ?? 0);
        var note = mrErr is null && cfErr is null
            ? (mr is null && cf is null ? "无响应" : null)
            : mrErr is null ? $"CurseForge 搜索失败（{cfErr}），仅显示 Modrinth 结果"
            : cfErr is null ? $"Modrinth 搜索失败（{mrErr}），仅显示 CurseForge 结果"
            : "双源搜索均失败";
        FinishPage(seq, total, gameVersion, note);
    }

    /// <summary>结果填充：中文 query 先按匹配质量重排（标题匹配&gt;描述匹配&gt;无），英文信任源排序</summary>
    private void AddCards<T>(IEnumerable<T> items, Func<T, string> titleOf, Func<T, string> descOf, Func<T, ProjectCardVM> toCard)
    {
        if (EcosystemService.IsChineseQuery(Query))
            items = EcosystemService.ReorderMatches(items, Query, titleOf, descOf);
        foreach (var x in items) Cards.Add(toCard(x));
    }

    /// <summary>单源搜索容错：失败只记录不抛（双源模式用）。取消必须向上（新请求竞态），不能吞。</summary>
    private static async Task<T?> TrySearchAsync<T>(Task<T> task, Action<Exception> onError)
    {
        try { return await task; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { onError(ex); return default; }
    }

    /// <summary>分页状态统一收尾（CF 无分页信息时总数=当前页条数，分页栏按此算）</summary>
    private void FinishPage(int seq, int total, string? gameVersion, string? errorStatus)
    {
        TotalPages = Math.Max(1, (total + PageSize - 1) / PageSize);
        HasPrev = CurrentPage > 0;
        HasNext = CurrentPage < TotalPages - 1;
        PageText = $"{CurrentPage + 1}/{TotalPages}";
        IsEmpty = Cards.Count == 0;
        Status = errorStatus ?? (gameVersion is not null
            ? $"共 {total} 个结果 · 已按 {gameVersion} 过滤"
            : $"共 {total} 个结果");
    }

    /// <summary>Modrinth 排序 → CF 排序（关注数无对应 → 相关度）</summary>
    private static CurseForgeService.SortIndex CfSortOf(EcosystemService.SortIndex? index) => index switch
    {
        EcosystemService.SortIndex.Downloads => CurseForgeService.SortIndex.Downloads,
        EcosystemService.SortIndex.Updated => CurseForgeService.SortIndex.Updated,
        EcosystemService.SortIndex.Newest => CurseForgeService.SortIndex.Newest,
        _ => CurseForgeService.SortIndex.Relevance,
    };

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
                var (source, rawId) = ProjectCardVM.ParseId(id);
                if (source == "curseforge")
                {
                    if (!int.TryParse(rawId, out var modId)) continue;
                    var p = await _cf.GetProjectAsync(modId, ct);
                    if (p is not null)
                    {
                        var card = new ProjectCardVM(p);
                        if (TypeMatches(card.Type.ToString())) Cards.Add(card);
                    }
                }
                else
                {
                    var detail = await _eco.GetProjectAsync(id, ct);
                    if (detail is not null && TypeMatches(detail.ProjectType))
                        Cards.Add(new ProjectCardVM(detail));
                }
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
        if (card.Source == "curseforge")
        {
            await InstallCfCardAsync(card, instance, gameVersion);
            return;
        }
        var loader = instance is not null && instance.LoaderBadge.Length > 0 ? instance.LoaderBadge
            : instance is not null ? EcosystemService.GuessLoader(instance.Name) : null;
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
                    $"要装 {deps.Count} 个前置：{list}", $"安装 {card.Title}", "全部安装", "仅主文件");
            }

            if (instance is null)
            {
                NotificationService.Error("先选目标实例");
                return;
            }
            var instanceName = instance.Name;
            DependencyInstallReport? report = null;
            var task = DownloadManager.Instance.Enqueue($"安装 {card.Title}", async (p, ct) =>
            {
                report = includeDeps
                    ? await _eco.InstallWithDependenciesAsync(card.Id, version, instanceName, card.Type,
                        gameVersion, loader, dp => p(dp), ct)
                    : await InstallMainOnlyAsync(card.Id, version, instanceName, card.Type, p, ct);
            });
            // 跳转①：入队即去下载记录看进度；完成后跳回本 tab（跳转②由下载中心统一处理）
            MainViewModel.Current?.NavigateToDownloadQueue($"download:{DownloadViewModel.TabFor(_type)}");
            await task.Completion;
            if (task.State == DownloadTaskState.Completed)
            {
                NotificationService.Success(
                    report is { Installed.Count: > 0 }
                        ? $"{card.Title} 安装完成 → {report.Installed[0].Path}"
                        : $"{card.Title} 安装完成", 4500);
            }
            else if (task.Error is { } err)
                NotificationService.Error(err);
        }
        catch (Exception ex)
        {
            NotificationService.Error($"安装失败: {ex.Message}");
        }
    }

    /// <summary>仅安装主文件（依赖可选跳过路径）；返回报告供路径 Toast</summary>
    private async Task<DependencyInstallReport?> InstallMainOnlyAsync(string projectId, ModrinthVersion version,
        string instanceName, ProjectType type, DownloadProgressHandler progress, CancellationToken ct)
    {
        var path = await _eco.InstallAsync(projectId, version, instanceName, type, dp => progress(dp), ct);
        var r = new DependencyInstallReport();
        r.Installed.Add(new InstalledDependency(projectId, version.Id, path));
        return r;
    }

    /// <summary>CurseForge 卡片一键安装：最佳文件匹配 → 依赖确认 → 全局下载中心执行 → Toast</summary>
    private async Task InstallCfCardAsync(ProjectCardVM card, VersionInstanceVM? instance, string? gameVersion)
    {
        if (!int.TryParse(ProjectCardVM.ParseId(card.Id).RawId, out var modId)) return;
        try
        {
            var file = await _cf.FindBestFileAsync(modId, gameVersion, CancellationToken.None);
            if (file is null)
            {
                NotificationService.Error($"{card.Title} 没有适配当前实例的文件");
                return;
            }

            var depCount = (file.dependencies ?? []).Count(d => d.relationType == 1);
            var includeDeps = true;
            if (depCount > 0 && DialogService.MainWindow() is { } owner)
            {
                includeDeps = await DialogService.Confirm(owner,
                    $"要装 {depCount} 个前置依赖", $"安装 {card.Title}", "全部安装", "仅主文件");
            }

            if (instance is null)
            {
                NotificationService.Error("先选目标实例");
                return;
            }
            var instanceName = instance.Name;
            DependencyInstallReport? report = null;
            var task = DownloadManager.Instance.Enqueue($"安装 {card.Title}", async (p, ct) =>
            {
                if (includeDeps)
                {
                    report = await _cf.InstallWithDependenciesAsync(modId, file, instanceName, card.Type,
                        gameVersion, dp => p(dp), ct);
                }
                else
                {
                    var path = await _cf.InstallAsync(modId, file, instanceName, card.Type, dp => p(dp), ct);
                    var r = new DependencyInstallReport();
                    r.Installed.Add(new InstalledDependency(modId.ToString(), file.id.ToString(), path));
                    report = r;
                }
            });
            // 跳转①：入队即去下载记录看进度；完成后跳回本 tab（跳转②由下载中心统一处理）
            MainViewModel.Current?.NavigateToDownloadQueue($"download:{DownloadViewModel.TabFor(_type)}");
            await task.Completion;
            if (task.State == DownloadTaskState.Completed)
            {
                NotificationService.Success(
                    report is { Installed.Count: > 0 }
                        ? $"{card.Title} 安装完成 → {report.Installed[0].Path}"
                        : $"{card.Title} 安装完成", 4500);
            }
            else if (task.Error is { } err)
                NotificationService.Error(err);
        }
        catch (Exception ex)
        {
            NotificationService.Error($"安装失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenDetail(ProjectCardVM card) =>
        Detail = new ProjectDetailViewModel(_eco, _cf, card, SelectedInstance, () => Detail = null);

    [RelayCommand]
    private void CloseDetail() => Detail = null;
}
