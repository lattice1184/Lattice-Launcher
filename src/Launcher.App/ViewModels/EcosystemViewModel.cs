using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

/// <summary>
/// 生态列表页：类型 Tab + 防抖搜索 + 实例过滤 + 卡片流 + 四态 + 分页。
/// </summary>
public partial class EcosystemViewModel : ViewModelBase
{
    private readonly EcosystemService _eco = new();
    private CancellationTokenSource? _searchCts;
    private int _requestSeq;
    private int _offset;

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

    // Tab 状态
    [ObservableProperty]
    public partial bool IsModTabSelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsModpackTabSelected { get; set; }

    [ObservableProperty]
    public partial bool IsResourcepackTabSelected { get; set; }

    [ObservableProperty]
    public partial bool IsShaderTabSelected { get; set; }

    private ProjectType CurrentType => IsModpackTabSelected ? ProjectType.Modpack
        : IsResourcepackTabSelected ? ProjectType.Resourcepack
        : IsShaderTabSelected ? ProjectType.Shader
        : ProjectType.Mod;

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
            string? gameVersion = null;
            string? loader = null;
            if (instance is not null)
            {
                if (EcosystemService.TryParseGameVersion(instance.Name, out var gv)) gameVersion = gv;
                loader = EcosystemService.GuessLoader(instance.Name);
            }

            var resp = await _eco.SearchAsync(CurrentType, Query, gameVersion, loader,
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
    private void SelectTab(string tab)
    {
        IsModTabSelected = tab == "mod";
        IsModpackTabSelected = tab == "modpack";
        IsResourcepackTabSelected = tab == "resourcepack";
        IsShaderTabSelected = tab == "shader";
        DebouncedSearch();
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
