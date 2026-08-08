using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Download;
using Launcher.Core.Model.Modrinth;

namespace Launcher.App.ViewModels;

/// <summary>
/// 下载板块：MOD / 整合包 / 材质包 / 光影包 / 下载记录（全局队列）。
/// 游戏版本下载已移至顶级"版本"导航页。tab 懒实例化：首次激活才创建对应 VM 并触发加载。
/// </summary>
public partial class DownloadViewModel : ViewModelBase
{
    private VersionDownloadViewModel? _game;
    private EcosystemViewModel? _mods;
    private EcosystemViewModel? _modpacks;
    private EcosystemViewModel? _resourcepacks;
    private EcosystemViewModel? _shaders;
    private ThirdPartyDownloadViewModel? _thirdParty;

    public ObservableCollection<DownloadTask> Tasks => DownloadManager.Instance.Tasks;

    /// <summary>下载历史（终态任务记录，跨会话保持）</summary>
    public ObservableCollection<DownloadHistoryEntry> History { get; } = [];

    private readonly HashSet<DownloadTask> _recorded = [];

    /// <summary>跳转②来源页（入队时经 NavigateToDownloadQueue(returnTo) 设置；任务终态跳回一次后清空）</summary>
    private string? _returnTo;

    /// <summary>记录任务完成后的跳回目标（null = 不跳回）</summary>
    public void SetReturnNavigation(string? returnTo) => _returnTo = returnTo;

    [ObservableProperty]
    public partial string Status { get; set; } = "暂无下载任务";

    /// <summary>导航角标文字（" 2"），ActiveCount > 0 时显示</summary>
    [ObservableProperty]
    public partial string ActiveBadgeText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasActive { get; set; }

    /// <summary>有已暂停任务（"继续"按钮显示）</summary>
    [ObservableProperty]
    public partial bool HasPaused { get; set; }

    /// <summary>当前 tab 内容（ContentControl 绑定；queue 用常驻面板不走这里）</summary>
    [ObservableProperty]
    public partial ViewModelBase? ActiveTab { get; set; }

    // Tab 高亮状态（默认 MOD）
    [ObservableProperty]
    public partial bool IsGameTabSelected { get; set; }

    [ObservableProperty]
    public partial bool IsModTabSelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsQueueTabSelected { get; set; }

    [ObservableProperty]
    public partial bool IsModpackTabSelected { get; set; }

    [ObservableProperty]
    public partial bool IsResourcepackTabSelected { get; set; }

    [ObservableProperty]
    public partial bool IsShaderTabSelected { get; set; }

    [ObservableProperty]
    public partial bool IsThirdPartyTabSelected { get; set; }

    /// <summary>内容区 ContentControl 显示条件（queue 用常驻面板，其余走懒 ContentControl）</summary>
    public bool IsNotQueueTabSelected => !IsQueueTabSelected;

    partial void OnIsQueueTabSelectedChanged(bool value) => OnPropertyChanged(nameof(IsNotQueueTabSelected));

    public DownloadViewModel()
    {
        DownloadManager.Instance.ActiveCountChanged += OnActiveChanged;
        DownloadManager.Instance.PausedChanged += v => HasPaused = v;
        DownloadHistoryService.Changed += ReloadHistory;
        HasPaused = DownloadManager.Instance.HasPaused;
        OnActiveChanged(DownloadManager.Instance.ActiveCount);
        ReloadHistory();
        // 任务终态 → 记入历史（每任务一次）
        Tasks.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (DownloadTask t in e.NewItems)
                t.PropertyChanged += OnTaskPropertyChanged;
        };
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DownloadTask.State)) return;
        if (sender is not DownloadTask t) return;
        if (t.State is not (DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled)) return;
        if (!_recorded.Add(t)) return; // 终态只记一次（暂停 Paused 不是终态）
        DownloadHistoryService.Record(t);
        // 完成/失败弹 Toast（滑入动画由 ToastHost 处理）
        if (t.State == DownloadTaskState.Completed)
            NotificationService.Success($"已完成：{t.Name}");
        else if (t.State == DownloadTaskState.Failed)
            NotificationService.Error($"失败：{t.Name}");

        // 跳转②：任务完成/失败 → 跳回来源页（只一次；取消不跳——用户主动取消留在记录）
        if (t.State is DownloadTaskState.Completed or DownloadTaskState.Failed && _returnTo is { } page)
        {
            _returnTo = null;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => MainViewModel.Current?.NavigateTo(page));
        }
    }

    private void ReloadHistory()
    {
        History.Clear();
        foreach (var h in DownloadHistoryService.All) History.Add(h);
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        var owner = DialogService.MainWindow();
        if (owner is null || !await DialogService.Confirm(owner, "清除全部下载历史？", "清除历史", "清除", "取消"))
            return;
        DownloadHistoryService.Clear();
    }

    /// <summary>切页进入时激活默认 tab（MainViewModel.Navigate 调用；首次进入下载页才触发加载）</summary>
    public void ActivateDefault()
    {
        if (ActiveTab is null) SelectTab("mod");
        PreloadTabs();
    }

    /// <summary>跳到"下载记录"tab（下载中"查看下载进度"链接用）</summary>
    public void NavigateToQueue() => SelectTab("queue");

    /// <summary>跳到"下载游戏"tab（版本页引导按钮用）</summary>
    public void NavigateToGame() => SelectTab("game");

    private int _preloadStarted;

    /// <summary>进入下载页后错峰预热 4 个资源 tab 数据（建 VM + 加载数据，不建视图）——切 tab 秒开无撕裂</summary>
    private async void PreloadTabs()
    {
        if (Interlocked.Exchange(ref _preloadStarted, 1) == 1) return;
        var tabs = new[] { "mod", "modpack", "resourcepack", "shader" };
        for (var i = 0; i < tabs.Length; i++)
        {
            await Task.Delay(300 * (i + 1));
            GetOrCreateTab(tabs[i]);
        }
    }

    [RelayCommand]
    public void SelectTab(string tab)
    {
        IsGameTabSelected = tab == "game";
        IsQueueTabSelected = tab == "queue";
        IsModTabSelected = tab == "mod";
        IsModpackTabSelected = tab == "modpack";
        IsResourcepackTabSelected = tab == "resourcepack";
        IsShaderTabSelected = tab == "shader";
        IsThirdPartyTabSelected = tab == "thirdparty";
        if (tab != "queue") ActiveTab = GetOrCreateTab(tab);
    }

    /// <summary>懒创建 tab VM：首次激活才 new 并触发加载（异步，列表区转圈）</summary>
    private ViewModelBase GetOrCreateTab(string tab) => tab switch
    {
        "game" => _game ??= CreateAndLoad(new VersionDownloadViewModel(), v => v.EnsureLoadedAsync()),
        "mod" => _mods ??= CreateAndLoad(new EcosystemViewModel(ProjectType.Mod), e => e.InitializeAsync()),
        "modpack" => _modpacks ??= CreateAndLoad(new EcosystemViewModel(ProjectType.Modpack), e => e.InitializeAsync()),
        "resourcepack" => _resourcepacks ??= CreateAndLoad(new EcosystemViewModel(ProjectType.Resourcepack), e => e.InitializeAsync()),
        "shader" => _shaders ??= CreateAndLoad(new EcosystemViewModel(ProjectType.Shader), e => e.InitializeAsync()),
        "thirdparty" => _thirdParty ??= new ThirdPartyDownloadViewModel(),
        _ => throw new ArgumentOutOfRangeException(nameof(tab)),
    };

    private static T CreateAndLoad<T>(T vm, Func<T, Task> load)
    {
        _ = load(vm);
        return vm;
    }

    private void OnActiveChanged(int active)
    {
        ActiveBadgeText = active > 0 ? $" {active}" : "";
        HasActive = active > 0;
        Status = Tasks.Count == 0
            ? "暂无下载任务"
            : active > 0 ? $"正在下载 {active} 个任务" : "下载任务已全部完成";
    }

    /// <summary>ProjectType → 下载页 tab 名（安装完成跳回原 tab 用）</summary>
    public static string TabFor(ProjectType type) => type switch
    {
        ProjectType.Mod => "mod",
        ProjectType.Modpack => "modpack",
        ProjectType.Resourcepack => "resourcepack",
        ProjectType.Shader => "shader",
        _ => "mod",
    };

    /// <summary>历史「重新下载」：同 URL 重下到原目录（同名自动 (1) 后缀，不覆盖）</summary>
    [RelayCommand]
    private void Redownload(DownloadHistoryEntry? entry)
    {
        if (entry?.SourceUrl is not { } url || entry.TargetPath is not { } dest) return;
        var name = UriFileNameResolver.FromUrl(url) ?? Path.GetFileName(dest);
        var target = UniquePath.Resolve(dest);
        DownloadManager.Instance.Enqueue($"重新下载 {name}", (p, ct) =>
            new DownloadService().DownloadFileAsync(url, target, null, null, p, ct));
    }

    /// <summary>历史「打开位置」：资源管理器定位文件</summary>
    [RelayCommand]
    private void OpenHistoryLocation(DownloadHistoryEntry? entry)
    {
        if (entry?.TargetPath is not { } dest) return;
        if (!File.Exists(dest))
        {
            NotificationService.Error("文件已不存在（可能被移动或删除）");
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dest}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void ClearFinished() => DownloadManager.Instance.ClearFinished();

    [RelayCommand]
    private void SuspendAll() => DownloadManager.Instance.SuspendAll();

    [RelayCommand]
    private void ResumeAll() => DownloadManager.Instance.ResumeAll();
}
