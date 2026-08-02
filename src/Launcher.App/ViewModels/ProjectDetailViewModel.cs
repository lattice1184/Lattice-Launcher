using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Download;
using Launcher.Core.Ecosystem;
using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

/// <summary>
/// 项目详情页：项目信息 + 截图画廊 + 版本匹配/手动选择 + 更新日志 + 一键安装（含依赖解析）。
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
    public partial string License { get; set; } = "";

    [ObservableProperty]
    public partial Bitmap? Icon { get; set; }

    [ObservableProperty]
    public partial Bitmap? Screenshot { get; set; }

    [ObservableProperty]
    public partial string Changelog { get; set; } = "";

    public ObservableCollection<VersionOptionVM> AllVersions { get; } = [];

    [ObservableProperty]
    public partial VersionOptionVM? SelectedVersion { get; set; }

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

    /// <summary>前置提示（"将安装 2 个前置：A、B"）；安装按钮文字随之更新</summary>
    [ObservableProperty]
    public partial string DependencyHint { get; set; } = "";

    /// <summary>下载中"查看下载进度"跳转</summary>
    [RelayCommand]
    private void GoToDownloadQueue() => MainViewModel.Current?.NavigateToDownloadQueue();

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
        CanInstall = false;
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
                    : $"未匹配到 {_instance.Name} 的版本，请选择其他实例或手动选版本")
                : $"匹配版本: {version.Name} ({version.VersionNumber})";
            CanInstall = version is not null;
            if (version is not null) Changelog = version.Changelog ?? "";
            if (version is not null) _ = ResolveDependencyHintAsync(version, gameVersion, loader);

            // 项目详情（截图/许可证）
            try
            {
                var detail = await _eco.GetProjectAsync(_card.Id);
                if (detail is not null)
                {
                    License = detail.License?.Name is { } ln ? $"许可: {ln}" : "";
                    if (detail.Gallery is { Count: > 0 })
                        _ = ImageLoader.LoadAsync(detail.Gallery[0], bmp => Screenshot = bmp, 640);
                }
            }
            catch { /* 详情拉取失败不阻塞 */ }
        }
        catch (Exception ex)
        {
            VersionHint = $"匹配失败: {ex.Message}";
        }
    }

    /// <summary>后台解析依赖：前置提示 + 安装按钮文字（"安装（含 N 个前置）"）</summary>
    private async Task ResolveDependencyHintAsync(ModrinthVersion version, string? gameVersion, string? loader)
    {
        try
        {
            var names = await Task.Run(() => _eco.ResolveDependencyNamesAsync(version, gameVersion, loader, CancellationToken.None));
            if (names.Count == 0)
            {
                DependencyHint = "无需前置依赖";
                return;
            }
            DependencyHint = $"将安装 {names.Count} 个前置：{string.Join("、", names)}";
            if (!IsInstalling && InstallButtonText == "安装")
                InstallButtonText = $"安装（含 {names.Count} 个前置）";
        }
        catch { /* 解析失败不阻塞安装 */ }
    }

    /// <summary>懒加载全部版本供手动选择</summary>
    [RelayCommand]
    private async Task LoadVersions()
    {
        if (AllVersions.Count > 0) return;
        try
        {
            string? gameVersion = null;
            string? loader = null;
            if (_instance is not null)
            {
                if (EcosystemService.TryParseGameVersion(_instance.Name, out var gv)) gameVersion = gv;
                loader = EcosystemService.GuessLoader(_instance.Name);
            }
            var versions = await _eco.GetVersionsAsync(_card.Id, gameVersion, loader);
            foreach (var v in versions.OrderByDescending(v => v.DatePublished))
                AllVersions.Add(new VersionOptionVM(v));
        }
        catch { }
    }

    partial void OnSelectedVersionChanged(VersionOptionVM? value)
    {
        if (value is null) return;
        _matchedVersion = value.Source;
        Changelog = value.Source.Changelog ?? "";
        VersionHint = $"已选择: {value.Source.Name} ({value.Source.VersionNumber})";
        CanInstall = true;
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
        ProgressState = "准备中…";

        try
        {
            if (_instance is null && _card.Type != ProjectType.Modpack)
                throw new InvalidOperationException("请先在生态页顶部选择目标实例");

            var version = _matchedVersion
                ?? throw new InvalidOperationException("没有匹配的可用版本");

            var gameVersion = EcosystemService.TryParseGameVersion(_instance?.Name ?? "", out var gv)
                ? gv
                : version.GameVersions?.FirstOrDefault() ?? "";
            var loader = EcosystemService.GuessLoader(_instance?.Name ?? "");
            var instanceName = _instance?.Name ?? "modpack";

            // 经全局下载中心执行（后台线程 + 队列 UI + 内联进度双显示，一处真相）
            DependencyInstallReport? report = null;
            var task = DownloadManager.Instance.Enqueue($"安装 {_card.Title}", async (p, t) =>
            {
                report = await _eco.InstallWithDependenciesAsync(_card.Id, version, instanceName, _card.Type,
                    gameVersion, loader,
                    dp => p(dp with { Stage = dp.CurrentFile is { } f ? $"下载 {f}" : "下载文件" }), t);
            });
            if (ct.CanBeCanceled) ct.Register(() => task.Cancel());

            // 内联进度区订阅同一任务属性
            void Sync(object? _, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(DownloadTask.ProgressPercent)) Progress = task.ProgressPercent;
                else if (e.PropertyName == nameof(DownloadTask.Stage)) ProgressState = task.Stage;
                else if (e.PropertyName == nameof(DownloadTask.State)) { ProgressState = task.StateText; IsInstalling = task.IsActive; }
                else if (e.PropertyName == nameof(DownloadTask.Error) && task.Error is { } err) ErrorMessage = err;
            }
            task.PropertyChanged += Sync;
            try { await task.Completion; }
            finally { task.PropertyChanged -= Sync; }

            if (ct.IsCancellationRequested)
            {
                ProgressState = "已取消";
            }
            else if (task.State == DownloadTaskState.Completed && report is { AllSucceeded: true })
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
            else if (task.State == DownloadTaskState.Completed)
            {
                var failed = report is null ? "" : string.Join("; ", report.Failed.Select(f => $"{f.ProjectId}: {f.Reason}"));
                ErrorMessage = $"部分安装失败: {failed}";
                ProgressState = "部分失败";
                InstallButtonText = "安装";
            }
            else if (task.State == DownloadTaskState.Failed)
            {
                ErrorMessage = task.Error ?? "未知错误";
                ProgressState = "安装失败";
                InstallButtonText = "安装";
            }
            else
            {
                ProgressState = task.StateText;
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

/// <summary>版本选项（手动选择用）</summary>
public sealed record VersionOptionVM(ModrinthVersion Source)
{
    public string Display
    {
        get
        {
            var games = Source.GameVersions is { Count: > 0 } ? string.Join("/", Source.GameVersions.Take(2)) : "?";
            var loaders = Source.Loaders is { Count: > 0 } ? string.Join("/", Source.Loaders.Take(2)) : "any";
            return $"{Source.VersionNumber} · {games} · {loaders}";
        }
    }
}
