using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    /// <summary>当前实例（其他页面跳转下载记录用）</summary>
    public static MainViewModel? Current { get; private set; }

    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    // 导航高亮（主页/版本/下载/账号/设置）
    [ObservableProperty]
    public partial bool IsHomeActive { get; set; } = true;

    [ObservableProperty]
    public partial bool IsVersionsActive { get; set; }

    [ObservableProperty]
    public partial bool IsDownloadsActive { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsActive { get; set; }

    [ObservableProperty]
    public partial bool IsServerActive { get; set; }

    public HomeViewModel Home { get; } = new();
    public VersionBrowseViewModel Versions { get; } = new();
    public DownloadViewModel Downloads { get; } = new();
    public SettingsViewModel Settings { get; } = new();
    public ServerViewModel Server { get; } = new();

    public MainViewModel()
    {
        Current = this;
        CurrentPage = Home;
        _ = Home.InitializeAsync();
    }

    /// <summary>跳到下载板块的"下载记录"tab（下载中"查看下载进度"链接用）</summary>
    public void NavigateToDownloadQueue()
    {
        Navigate("download");
        Downloads.NavigateToQueue();
    }

    /// <summary>跳到下载板块的"下载游戏"tab（版本页引导按钮用）</summary>
    public void NavigateToDownloadGame()
    {
        Navigate("download");
        Downloads.NavigateToGame();
    }

    /// <summary>从版本页启动某版本：切主页并自动启动（版本页行 [启动] 按钮）</summary>
    public void LaunchVersion(string versionId, string gameDir)
    {
        Navigate("home");
        _ = Home.RequestLaunchAsync(versionId, gameDir);
    }

    /// <summary>跳版本页并选中指定版本（主页启动失败"去版本页补全"用；先等版本列表加载完成再选中——否则首次导航 _all 为空选不中）</summary>
    public async Task NavigateToVersionAsync(string? id = null)
    {
        Navigate("version");
        if (string.IsNullOrEmpty(id)) return;
        await Versions.EnsureLoadedAsync();
        Versions.SelectById(id);
    }

    /// <summary>停止游戏（版本页 [停止]）</summary>
    public void StopGame() => Home.StopGameCommand.Execute(null);

    [RelayCommand]
    private void Navigate(string page)
    {
        IsHomeActive = page == "home";
        IsVersionsActive = page == "version";
        IsDownloadsActive = page == "download";
        IsSettingsActive = page == "settings";
        IsServerActive = page == "server";
        if (page == "download") Downloads.ActivateDefault();
        if (page == "home") { Home.RefreshConfigText(); _ = Home.RefreshVersionsAsync(); } // 切回主页刷新配置摘要+已装版本
        if (page == "version") _ = Versions.EnsureLoadedAsync(); // 首次进入版本页才拉清单
        if (page == "server") _ = Server.RefreshVersionsAsync(); // 每次进开服页刷新已装版本（新装的立即可见）
        CurrentPage = page switch
        {
            "version" => Versions,
            "download" => Downloads,
            "settings" => Settings,
            "server" => Server,
            _ => Home,
        };
    }
}
