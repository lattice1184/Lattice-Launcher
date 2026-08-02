using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Download;
using Launcher.Core.Model.Modrinth;

namespace Launcher.App.ViewModels;

/// <summary>
/// 下载板块：下载记录（全局队列）+ 资源下载（MOD/整合包/材质包/光影包，各一个 EcosystemViewModel 实例）。
/// </summary>
public partial class DownloadViewModel : ViewModelBase
{
    public ObservableCollection<DownloadTask> Tasks => DownloadManager.Instance.Tasks;

    // 资源下载面板（每种类型一个实例，tab 切换显示）
    public EcosystemViewModel Mods { get; }
    public EcosystemViewModel Modpacks { get; }
    public EcosystemViewModel Resourcepacks { get; }
    public EcosystemViewModel Shaders { get; }

    [ObservableProperty]
    public partial string Status { get; set; } = "暂无下载任务";

    /// <summary>导航角标文字（" 2"），ActiveCount > 0 时显示</summary>
    [ObservableProperty]
    public partial string ActiveBadgeText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasActive { get; set; }

    // Tab 状态（与 MainViewModel 导航同款模式）
    [ObservableProperty]
    public partial bool IsQueueTabSelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsModTabSelected { get; set; }

    [ObservableProperty]
    public partial bool IsModpackTabSelected { get; set; }

    [ObservableProperty]
    public partial bool IsResourcepackTabSelected { get; set; }

    [ObservableProperty]
    public partial bool IsShaderTabSelected { get; set; }

    public DownloadViewModel()
    {
        Mods = new EcosystemViewModel(ProjectType.Mod);
        Modpacks = new EcosystemViewModel(ProjectType.Modpack);
        Resourcepacks = new EcosystemViewModel(ProjectType.Resourcepack);
        Shaders = new EcosystemViewModel(ProjectType.Shader);
        _ = Mods.InitializeAsync();
        _ = Modpacks.InitializeAsync();
        _ = Resourcepacks.InitializeAsync();
        _ = Shaders.InitializeAsync();

        DownloadManager.Instance.ActiveCountChanged += OnActiveChanged;
        OnActiveChanged(DownloadManager.Instance.ActiveCount);
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        IsQueueTabSelected = tab == "queue";
        IsModTabSelected = tab == "mod";
        IsModpackTabSelected = tab == "modpack";
        IsResourcepackTabSelected = tab == "resourcepack";
        IsShaderTabSelected = tab == "shader";
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
