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
    public partial bool IsAccountActive { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsActive { get; set; }

    public HomeViewModel Home { get; } = new();
    public VersionBrowseViewModel Versions { get; } = new();
    public DownloadViewModel Downloads { get; } = new();
    public AccountViewModel Account { get; } = new();
    public SettingsViewModel Settings { get; } = new();

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

    [RelayCommand]
    private void Navigate(string page)
    {
        IsHomeActive = page == "home";
        IsVersionsActive = page == "version";
        IsDownloadsActive = page == "download";
        IsAccountActive = page == "account";
        IsSettingsActive = page == "settings";
        if (page == "download") Downloads.ActivateDefault();
        if (page == "home") { Home.RefreshConfigText(); _ = Home.RefreshVersionsAsync(); } // 切回主页刷新配置摘要+已装版本
        if (page == "version") _ = Versions.EnsureLoadedAsync(); // 首次进入版本页才拉清单
        CurrentPage = page switch
        {
            "version" => Versions,
            "download" => Downloads,
            "account" => Account,
            "settings" => Settings,
            _ => Home,
        };
    }
}
