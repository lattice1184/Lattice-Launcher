using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private int _offset;

    public EcosystemViewModel(ProjectType type = ProjectType.Mod)
    {
        _type = type;
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

    /// <summary>游戏版本下拉（"跟随实例"=null + 常用版本）</summary>
    public static IReadOnlyList<string> GameVersionOptions { get; } =
        ["跟随实例", "1.21.6", "1.21.5", "1.21.4", "1.21.3", "1.21.1", "1.20.4", "1.20.1", "1.19.4", "1.18.2"];

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

    /// <summary>游戏版本筛选（null=跟随实例解析）</summary>
    [ObservableProperty]
    public partial string? SelectedGameVersion { get; set; }

    /// <summary>功能分类筛选（null=全部）</summary>
    [ObservableProperty]
    public partial CategoryOption? SelectedCategory { get; set; }

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

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial ProjectDetailViewModel? Detail { get; set; }

    [ObservableProperty]
    public partial bool IsDetailOpen { get; set; }

    partial void OnDetailChanged(ProjectDetailViewModel? value) => IsDetailOpen = value is not null;

    partial void OnSelectedLoaderChanged(string? value) => DebouncedSearch();
    partial void OnSelectedGameVersionChanged(string? value) => DebouncedSearch();
    partial void OnSelectedCategoryChanged(CategoryOption? value) => DebouncedSearch();

    /// <summary>加载器 chips 选择（"全部"=null；仅 MOD 类型显示）</summary>
    [RelayCommand]
    private void SelectLoader(string loader) => SelectedLoader = loader == "全部" ? null : loader;

    /// <summary>初始化：扫描已装实例并触发首搜</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var svc = new VersionManifestService();
            await svc.RefreshAsync();
            Instances.Clear();
            foreach (var e in svc.Entries.Where(e => e.Installed))
                Instances.Add(new VersionInstanceVM(e.Id));
        }
        catch { /* 实例扫描失败不阻塞搜索 */ }

        if (Instances.Count > 0) SelectedInstance = Instances[0];
        await RunSearchAsync(reset: true);
    }

    partial void OnQueryChanged(string value) => DebouncedSearch();

    /// <summary>防抖搜索（400ms，取消旧请求）</summary>
    private async void DebouncedSearch()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(400, cts.Token);
            await RunSearchAsync(reset: true, cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunSearchAsync(bool reset, CancellationToken ct = default)
    {
        var seq = ++_requestSeq;
        if (reset)
        {
            Cards.Clear();
            _offset = 0;
        }
        IsLoading = true;
        IsError = false;
        IsEmpty = false;
        try
        {
            var instance = SelectedInstance;
            // 三级筛选：显式选择优先，否则跟随实例（加载器猜测/版本解析）
            var loader = SelectedLoader
                ?? (instance is not null ? EcosystemService.GuessLoader(instance.Name) : null);
            var gameVersion = SelectedGameVersion
                ?? (instance is not null && EcosystemService.TryParseGameVersion(instance.Name, out var gv) ? gv : null);
            var category = SelectedCategory?.Key;

            var resp = await _eco.SearchAsync(_type, Query, gameVersion, loader, category,
                limit: 20, offset: _offset, ct);
            if (seq != _requestSeq) return; // 竞态：旧响应直接丢弃

            var hits = resp?.Hits ?? [];
            foreach (var h in hits) Cards.Add(new ProjectCardVM(h));
            _offset += hits.Count;
            HasMore = _offset < (resp?.TotalHits ?? 0);
            IsEmpty = Cards.Count == 0;
            Status = resp is null ? "无响应" : $"共 {resp.TotalHits} 个结果";
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

    [RelayCommand]
    private Task Search(bool reset) => RunSearchAsync(reset);

    [RelayCommand]
    private Task LoadMore() => RunSearchAsync(reset: false);

    [RelayCommand]
    private void OpenDetail(ProjectCardVM card) =>
        Detail = new ProjectDetailViewModel(_eco, card, SelectedInstance, () => Detail = null);

    [RelayCommand]
    private void CloseDetail() => Detail = null;
}
