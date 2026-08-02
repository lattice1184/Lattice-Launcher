using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    // 导航高亮
    [ObservableProperty]
    public partial bool IsHomeActive { get; set; } = true;

    [ObservableProperty]
    public partial bool IsVersionsActive { get; set; }

    [ObservableProperty]
    public partial bool IsDownloadsActive { get; set; }

    [ObservableProperty]
    public partial bool IsAccountActive { get; set; }

    public HomeViewModel Home { get; } = new();
    public VersionBrowseViewModel Versions { get; } = new();
    public DownloadViewModel Downloads { get; } = new();
    public AccountViewModel Account { get; } = new();

    public MainViewModel()
    {
        CurrentPage = Home;
        _ = Versions.LoadAsync();
        _ = Home.InitializeAsync();
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        IsHomeActive = page == "home";
        IsVersionsActive = page == "versions";
        IsDownloadsActive = page == "download";
        IsAccountActive = page == "account";
        CurrentPage = page switch
        {
            "versions" => Versions,
            "download" => Downloads,
            "account" => Account,
            _ => Home,
        };
    }
}
