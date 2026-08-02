using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Download;

namespace Launcher.App.ViewModels;

/// <summary>下载页：绑定全局下载中心队列（版本/加载器/模组所有下载统一在此显示）</summary>
public partial class DownloadViewModel : ViewModelBase
{
    public ObservableCollection<DownloadTask> Tasks => DownloadManager.Instance.Tasks;

    [ObservableProperty]
    public partial string Status { get; set; } = "暂无下载任务";

    /// <summary>导航角标文字（" 2"），ActiveCount > 0 时显示</summary>
    [ObservableProperty]
    public partial string ActiveBadgeText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasActive { get; set; }

    public DownloadViewModel()
    {
        DownloadManager.Instance.ActiveCountChanged += OnActiveChanged;
        OnActiveChanged(DownloadManager.Instance.ActiveCount);
    }

    private void OnActiveChanged(int active)
    {
        ActiveBadgeText = active > 0 ? $" {active}" : "";
        HasActive = active > 0;
        Status = Tasks.Count == 0
            ? "暂无下载任务"
            : active > 0 ? $"正在下载 {active} 个任务" : "下载任务已全部完成";
    }

    [RelayCommand]
    private void ClearFinished() => DownloadManager.Instance.ClearFinished();
}
