using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels;

public partial class DownloadViewModel : ViewModelBase
{
    public ObservableCollection<DownloadTaskVM> Tasks { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "暂无下载任务";
}

public partial class DownloadTaskVM : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string State { get; set; } = "等待";

    public DownloadTaskVM(string name) => Name = name;
}
