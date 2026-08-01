using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Ecosystem;
using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

/// <summary>
/// 项目详情页：显示项目信息 + 自动匹配版本 + 一键安装（含依赖解析，后台线程防死锁）。
/// </summary>
public partial class ProjectDetailViewModel : ViewModelBase
{
    private readonly EcosystemService _eco;
    private readonly ProjectCardVM _card;
    private readonly VersionInstanceVM? _instance;
    private readonly Action _closeCallback;
    private ModrinthVersion? _matchedVersion;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Author { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    [ObservableProperty]
    public partial string Stats { get; set; }

    [ObservableProperty]
    public partial string IconUrl { get; set; }

    [ObservableProperty]
    public partial string VersionHint { get; set; } = "匹配版本中…";

    [ObservableProperty]
    public partial Bitmap? Icon { get; set; }

    // 安装状态
    [ObservableProperty]
    public partial bool CanInstall { get; set; }

    [ObservableProperty]
    public partial string InstallButtonText { get; set; } = "安装";

    [ObservableProperty]
    public partial bool IsInstalling { get; set; }

    [ObservableProperty]
    public partial bool InstallDone { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string ProgressState { get; set; } = "";

    [ObservableProperty]
    public partial string InstalledPath { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    [ObservableProperty]
    public partial string DependenciesText { get; set; } = "";

    public ProjectDetailViewModel(EcosystemService eco, ProjectCardVM card, VersionInstanceVM? instance, Action closeCallback)
    {
        _eco = eco;
        _card = card;
        _instance = instance;
        _closeCallback = closeCallback;
        Title = card.Title;
        Author = card.Author;
        Description = card.Description;
        Stats = $"{card.DownloadsText} 下载 · {card.FollowsText} 关注";
        IconUrl = card.IconUrl;
        CanInstall = false; // 版本匹配完成前不可安装
        _ = ImageLoader.LoadAsync(IconUrl, bmp => Icon = bmp);
        _ = LoadAsync();
    }

    [RelayCommand]
    private void Close() => _closeCallback();

    private async Task LoadAsync()
    {
        try
        {
            string? gameVersion = null;
            string? loader = null;
            if (_instance is not null)
            {
                if (EcosystemService.TryParseGameVersion(_instance.Name, out var gv)) gameVersion = gv;
                loader = EcosystemService.GuessLoader(_instance.Name);
            }
            var version = await _eco.FindBestVersionAsync(_card.Id, gameVersion, loader);
            _matchedVersion = version;
            VersionHint = version is null
                ? (_instance is null
                    ? "未指定实例，可安装整合包或选择实例后安装"
                    : $"未匹配到 {_instance.Name} 的版本，请选择其他实例")
                : $"匹配版本: {version.Name} ({version.VersionNumber})";
            CanInstall = version is not null;
        }
        catch (Exception ex)
        {
            VersionHint = $"匹配失败: {ex.Message}";
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Install(CancellationToken ct)
    {
        IsInstalling = true;
        InstallDone = false;
        ErrorMessage = "";
        CanInstall = false;
        InstallButtonText = "取消";
        Progress = 0;

        try
        {
            if (_instance is null && _card.Type != ProjectType.Modpack)
                throw new InvalidOperationException("请先在生态页顶部选择目标实例");

            var version = _matchedVersion
                ?? throw new InvalidOperationException("没有匹配的可用版本");

            // 整体在后台线程执行：下载 + 依赖解析（适配器同步等待无 SynchronizationContext 死锁）
            var report = await Task.Run(async () =>
            {
                var progress = new Progress<double>(p => Progress = p);
                var gameVersion = EcosystemService.TryParseGameVersion(_instance?.Name ?? "", out var gv)
                    ? gv
                    : version.GameVersions?.FirstOrDefault() ?? "";
                var loader = EcosystemService.GuessLoader(_instance?.Name ?? "");
                ProgressState = "正在下载并解析依赖…";
                return await _eco.InstallWithDependenciesAsync(
                    _card.Id, version, _instance?.Name ?? "modpack", _card.Type,
                    gameVersion, loader, progress, ct);
            }, ct);

            if (ct.IsCancellationRequested)
            {
                ProgressState = "已取消";
            }
            else if (report.AllSucceeded)
            {
                InstalledPath = report.Installed.Count > 0 ? report.Installed[0].Path : "";
                var depCount = report.Installed.Count - 1;
                DependenciesText = depCount > 0
                    ? $"已安装 {depCount} 个依赖"
                    : report.Failed.Count > 0
                        ? $"{report.Failed.Count} 个依赖解析失败（不影响主文件）"
                        : "";
                InstallDone = true;
                ProgressState = "安装完成";
                Progress = 100;
                InstallButtonText = "已安装";
            }
            else
            {
                var failed = string.Join("; ", report.Failed.Select(f => $"{f.ProjectId}: {f.Reason}"));
                ErrorMessage = $"部分安装失败: {failed}";
                ProgressState = "部分失败";
                InstallButtonText = "安装";
            }
        }
        catch (OperationCanceledException)
        {
            ProgressState = "已取消";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ProgressState = "安装失败";
        }
        finally
        {
            IsInstalling = false;
            if (!InstallDone) CanInstall = true;
        }
    }
}
