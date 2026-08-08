using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Download;
using Launcher.Core.Ecosystem;
using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;
using PCL.Core.Minecraft.ResourceProject.Curseforge;

namespace Launcher.App.ViewModels;

/// <summary>
/// 项目详情页：项目信息 + 截图画廊 + 版本匹配/手动选择 + 更新日志 + 一键安装（含依赖解析）。
/// Modrinth / CurseForge 双源：按 card.Source 分支。
/// </summary>
public partial class ProjectDetailViewModel : ViewModelBase
{
    private readonly EcosystemService _eco;
    private readonly CurseForgeService _cf;
    private readonly ProjectCardVM _card;
    private readonly VersionInstanceVM? _instance;
    private readonly Action _closeCallback;
    private ModrinthVersion? _matchedVersion;
    private CurseforgeFile? _cfFile;
    private int _cfModId;

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

    // ---------- 截图画廊（左右切换） ----------

    private List<string> _galleryUrls = [];

    [ObservableProperty]
    public partial int GalleryIndex { get; set; }

    [ObservableProperty]
    public partial bool HasGallery { get; set; }

    [ObservableProperty]
    public partial string GalleryCountText { get; set; } = "";

    public bool HasPrevScreenshot => GalleryIndex > 0;
    public bool HasNextScreenshot => GalleryIndex < _galleryUrls.Count - 1;

    [RelayCommand]
    private void PrevScreenshot()
    {
        if (GalleryIndex <= 0) return;
        GalleryIndex--;
        LoadScreenshot(GalleryIndex);
    }

    [RelayCommand]
    private void NextScreenshot()
    {
        if (GalleryIndex >= _galleryUrls.Count - 1) return;
        GalleryIndex++;
        LoadScreenshot(GalleryIndex);
    }

    partial void OnGalleryIndexChanged(int value)
    {
        OnPropertyChanged(nameof(HasPrevScreenshot));
        OnPropertyChanged(nameof(HasNextScreenshot));
    }

    /// <summary>载入第 index 张截图（去重防闪烁：先清再载）</summary>
    private void LoadScreenshot(int index)
    {
        Screenshot = null;
        if (index < 0 || index >= _galleryUrls.Count) return;
        _ = ImageLoader.LoadAsync(_galleryUrls[index], bmp => Screenshot = bmp, 640);
    }

    // ---------- 文件列表（当前所选版本的安装文件） ----------

    public ObservableCollection<VersionFileVM> Files { get; } = [];

    [ObservableProperty]
    public partial string FilesHeaderText { get; set; } = "";

    /// <summary>文件区显示条件（有文件才展开）</summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>项目主页 URL（详情页"打开主页"）</summary>
    [ObservableProperty]
    public partial string ProjectPageUrl { get; set; } = "";

    /// <summary>浏览器打开项目主页（source_url 或 Modrinth 页面）</summary>
    public void OpenProjectPage()
    {
        if (string.IsNullOrEmpty(ProjectPageUrl)) return;
        try { Process.Start(new ProcessStartInfo(ProjectPageUrl) { UseShellExecute = true }); }
        catch { }
    }

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

    public ProjectDetailViewModel(EcosystemService eco, CurseForgeService cf, ProjectCardVM card,
        VersionInstanceVM? instance, Action closeCallback)
    {
        _eco = eco;
        _cf = cf;
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
        if (_card.Source == "curseforge")
        {
            await LoadCfAsync();
            return;
        }
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
                    ? "未选择目标实例，请先在生态页顶部选择实例。"
                    : $"没有 {_instance.Name} 能用的版本，换个实例或手动选版本")
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
                    ProjectPageUrl = detail.SourceUrl ?? $"https://modrinth.com/project/{detail.Slug}";
                    _galleryUrls = detail.Gallery ?? [];
                    HasGallery = _galleryUrls.Count > 1;
                    GalleryCountText = _galleryUrls.Count > 1 ? $"1/{_galleryUrls.Count}" : "";
                    GalleryIndex = 0;
                    if (_galleryUrls.Count > 0) LoadScreenshot(0);
                }
            }
            catch { /* 详情拉取失败不阻塞 */ }
        }
        catch (Exception ex)
        {
            VersionHint = $"匹配失败: {ex.Message}";
        }
    }

    /// <summary>CurseForge 详情：项目信息 + 最佳文件匹配 + 依赖计数（CF 无 changelog/关注字段）</summary>
    private async Task LoadCfAsync()
    {
        try
        {
            if (!int.TryParse(ProjectCardVM.ParseId(_card.Id).RawId, out var modId)) return;
            _cfModId = modId;
            string? gameVersion = null;
            if (_instance is not null && EcosystemService.TryParseGameVersion(_instance.Name, out var gv))
                gameVersion = gv;

            var file = await _cf.FindBestFileAsync(modId, gameVersion);
            _cfFile = file;
            VersionHint = file is null
                ? (_instance is null
                    ? "未选择目标实例，请先在生态页顶部选择实例。"
                    : $"未匹配到 {_instance.Name} 的版本")
                : $"匹配文件: {file.fileName}";
            CanInstall = file is not null;
            if (file is not null)
            {
                var depCount = (file.dependencies ?? []).Count(d => d.relationType == 1);
                DependencyHint = depCount == 0 ? "无需前置依赖" : $"将安装 {depCount} 个前置依赖";
            }

            try
            {
                var detail = await _cf.GetProjectAsync(modId);
                if (detail is not null)
                {
                    Title = detail.name;
                    Author = detail.authors is { Count: > 0 } ? string.Join("、", detail.authors.Select(a => a.name)) : "";
                    Description = detail.summary ?? "";
                    Stats = $"{ProjectCardVM.FormatCount(detail.downloadCount)} 下载";
                    IconUrl = detail.logo?.thumbnailUrl ?? "";
                    ProjectPageUrl = detail.links?.websiteUrl is { Length: > 0 } u
                        ? u
                        : $"https://www.curseforge.com/minecraft/mc-mods/{detail.slug}";
                    _galleryUrls = (detail.screenshots ?? []).Select(s => s.thumbnailUrl).ToList();
                    HasGallery = _galleryUrls.Count > 1;
                    GalleryCountText = _galleryUrls.Count > 1 ? $"1/{_galleryUrls.Count}" : "";
                    GalleryIndex = 0;
                    if (_galleryUrls.Count > 0) LoadScreenshot(0);
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
        if (_card.Source == "curseforge")
        {
            NotificationService.Info("CurseForge 自动选择最佳文件，暂不支持手动选版");
            return;
        }
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
        RefreshFiles(value.Source);
    }

    /// <summary>文件列表：主文件 + 附带文件（名称/大小）</summary>
    private void RefreshFiles(ModrinthVersion version)
    {
        Files.Clear();
        if (version.Files is null) return;
        foreach (var f in version.Files)
            Files.Add(new VersionFileVM(f.FileName, f.Size));
        FilesHeaderText = Files.Count > 0 ? $"文件（{Files.Count}）" : "";
        OnPropertyChanged(nameof(HasFiles));
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

        if (_card.Source == "curseforge")
        {
            await InstallCfAsync(ct);
            return;
        }

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
            // MOD 落点：版本来源目录（PCL 扫描版本 → PCL 目录；自建版本 → 自建目录）——AF2
            var gameDirFor = _instance is { GameDir.Length: > 0 } inst
                ? inst.GameDir
                : Launcher.Core.Utils.GameDirectory.InstallDir();

            // 依赖可选跳过：全部安装 / 仅主文件（依赖数来自安装前的解析提示）
            var includeDeps = true;
            if (DependencyHint.Length > 0 && DialogService.MainWindow() is { } owner)
            {
                includeDeps = await DialogService.Confirm(owner,
                    DependencyHint, $"安装 {_card.Title}", "全部安装", "仅主文件");
            }

            // 冲突提示：目标文件夹已有同名文件 / 已安装同 mod（fabric.mod.json id 匹配）——AF3
            if (_card.Type != ProjectType.Modpack && !await EnsureNoConflictAsync(gameDirFor, instanceName, version))
                return;

            // 经全局下载中心执行（后台线程 + 队列 UI + 内联进度双显示，一处真相）
            await ExecuteInstallAsync(async (dp, t) =>
            {
                if (includeDeps)
                    return await _eco.InstallWithDependenciesAsync(_card.Id, version, instanceName, _card.Type,
                        gameVersion, loader, dp, t, gameDirOverride: gameDirFor);
                var path = await _eco.InstallAsync(_card.Id, version, instanceName, _card.Type, dp, t, gameDirFor);
                var r = new DependencyInstallReport();
                r.Installed.Add(new InstalledDependency(_card.Id, version.Id, path));
                return r;
            }, instanceName, ct);
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

    /// <summary>冲突提示（AF3）：目标目录已有同名文件 / 已安装同 mod（fabric.mod.json id 匹配）→ 确认弹窗；false = 取消安装</summary>
    private async Task<bool> EnsureNoConflictAsync(string gameDir, string instanceId, ModrinthVersion version)
    {
        var owner = DialogService.MainWindow();
        if (owner is null) return true;
        var targetDir = EcosystemService.ResolveInstallPath(gameDir, instanceId, _card.Type);
        // 同名文件
        var fileName = version.Files?.FirstOrDefault()?.FileName ?? "";
        if (fileName.Length > 0 && File.Exists(Path.Combine(targetDir, fileName)))
            return await DialogService.Confirm(owner,
                $"目标文件夹已有同名文件：{fileName}\n覆盖下载？", "文件已存在", "覆盖", "取消");
        // 同 mod id（扫描 mods 下 jar 的 fabric.mod.json）
        if (_card.Type == ProjectType.Mod && Directory.Exists(targetDir))
        {
            foreach (var jar in Directory.EnumerateFiles(targetDir, "*.jar"))
            {
                if (JarModId(jar) != _card.Id) continue;
                return await DialogService.Confirm(owner,
                    $"「{_card.Title}」已经装在这个版本的 mods 文件夹里（检测到 {Path.GetFileName(jar)}）。\n还要下载？",
                    "已安装此模组", "仍要下载", "取消");
            }
        }
        return true;
    }

    /// <summary>读 jar 的 fabric.mod.json id（Forge mods 无此文件返回空；读取失败静默）</summary>
    private static string JarModId(string jarPath)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(jarPath);
            var entry = zip.GetEntry("fabric.mod.json") ?? zip.GetEntry("META-INF/fabric.mod.json");
            if (entry is null) return "";
            using var sr = new StreamReader(entry.Open());
            var doc = System.Text.Json.JsonDocument.Parse(sr.ReadToEnd());
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    /// <summary>经全局下载中心执行安装：队列 + 内联进度同步 + 状态收尾（Modrinth/CurseForge 共用）</summary>
    private async Task ExecuteInstallAsync(
        Func<DownloadProgressHandler, CancellationToken, Task<DependencyInstallReport?>> work,
        string instanceName, CancellationToken ct)
    {
        DependencyInstallReport? report = null;
        var task = DownloadManager.Instance.Enqueue($"安装 {_card.Title}", async (p, t) =>
        {
            report = await work(dp => p(dp with { Stage = dp.CurrentFile is { } f ? $"下载 {f}" : "下载文件" }), t);
        });
        // 跳转①：入队即去下载记录看进度；完成后跳回本 tab（详情层叠还在，跳转②由下载中心统一处理）
        MainViewModel.Current?.NavigateToDownloadQueue($"download:{DownloadViewModel.TabFor(_card.Type)}");
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
            // 长通知告知保存位置（Toast 支持换行；用户明确要求知道文件放哪了）
            if (InstalledPath.Length > 0 && _card.Type != ProjectType.Modpack)
                NotificationService.Success($"已安装到：{InstalledPath}");
            if (_card.Type == ProjectType.Modpack)
                NotificationService.Info("整合包已保存至 downloads/modpacks，请在【版本】页使用「导入整合包」创建实例。");
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

    /// <summary>CurseForge 安装：最佳匹配文件 → 依赖确认 → 共享执行管道</summary>
    private async Task InstallCfAsync(CancellationToken ct)
    {
        try
        {
            if (_instance is null && _card.Type != ProjectType.Modpack)
                throw new InvalidOperationException("请先在生态页顶部选择目标实例");
            var file = _cfFile ?? throw new InvalidOperationException("没有匹配的可用文件");
            var gameVersion = _instance is not null && EcosystemService.TryParseGameVersion(_instance.Name, out var gv)
                ? gv : null;
            var instanceName = _instance?.Name ?? "modpack";

            var includeDeps = true;
            if (DependencyHint.Length > 0 && DialogService.MainWindow() is { } owner)
            {
                includeDeps = await DialogService.Confirm(owner,
                    DependencyHint, $"安装 {_card.Title}", "全部安装", "仅主文件");
            }

            await ExecuteInstallAsync(async (dp, t) =>
            {
                if (includeDeps)
                    return await _cf.InstallWithDependenciesAsync(_cfModId, file, instanceName, _card.Type,
                        gameVersion, dp, t);
                var path = await _cf.InstallAsync(_cfModId, file, instanceName, _card.Type, dp, t);
                var r = new DependencyInstallReport();
                r.Installed.Add(new InstalledDependency(_card.Id, file.id.ToString(), path));
                return r;
            }, instanceName, ct);
        }
        catch (OperationCanceledException) { ProgressState = "已取消"; }
        catch (Exception ex) { ErrorMessage = ex.Message; ProgressState = "安装失败"; }
        finally { IsInstalling = false; if (!InstallDone) CanInstall = true; }
    }
}

/// <summary>版本选项（手动选择用）；推荐（Featured）标记 + 发布时间</summary>
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

    public bool IsRecommended => Source.Featured == true;

    public string PublishedText => Source.DatePublished.Year > 2000 ? Source.DatePublished.ToString("yyyy-MM-dd") : "";
}

/// <summary>版本文件行（文件名/大小）</summary>
public sealed record VersionFileVM(string Name, long SizeBytes)
{
    public string SizeText => SizeBytes >= 1024 * 1024
        ? $"{SizeBytes / 1024.0 / 1024:0.#} MB"
        : SizeBytes >= 1024 ? $"{SizeBytes / 1024:0} KB" : $"{SizeBytes} B";
}
