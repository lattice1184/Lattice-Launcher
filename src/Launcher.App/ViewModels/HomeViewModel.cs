using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Account;
using Launcher.Core.Download;
using Launcher.Core.Launch;
using Launcher.Core.Services;
using Launcher.Core.Utils;

namespace Launcher.App.ViewModels;

/// <summary>
/// 主页：玩家信息 + 版本选择 + 启动状态机（阶段指示条）+ 游戏控制台。
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    private static readonly string[] StageNames =
        ["解析版本", "检测 Java", "解压 natives", "启动 JVM", "游戏加载中", "运行中"];

    private readonly GameLaunchService _launcher = new();
    private readonly AccountService _accounts = AccountService.Shared;
    private LaunchProcess.LaunchResult? _running;
    private const int MaxLogLines = 500;
    private volatile bool _userStopped;

    public ObservableCollection<VersionInstanceVM> InstalledVersions { get; } = [];
    public ObservableCollection<LaunchStageVM> Stages { get; } = [];

    [ObservableProperty]
    public partial VersionInstanceVM? SelectedVersion { get; set; }

    [ObservableProperty]
    public partial string LaunchState { get; set; } = "就绪";

    /// <summary>启动状态圆点颜色（状态语言与阶段指示条一致：灰=待机、青=进行、红=失败）</summary>
    public IBrush StateColor
    {
        get
        {
            var s = LaunchState;
            if (s == "失败" || s.StartsWith("异常退出")) return new SolidColorBrush(Color.Parse("#E05A5A"));
            if (s is "运行中" or "准备中") return new SolidColorBrush(Color.Parse("#2DD4BF"));
            if (s.StartsWith("已退出")) return new SolidColorBrush(Color.Parse("#6F7B90"));
            return new SolidColorBrush(Color.Parse("#3A4250"));
        }
    }

    partial void OnLaunchStateChanged(string value) => OnPropertyChanged(nameof(StateColor));

    [ObservableProperty]
    public partial string LaunchStatus { get; set; } = "选择版本并启动";

    /// <summary>启动失败且为客户端文件缺失时显示修复入口（去版本页补全 / 官方下载）</summary>
    [ObservableProperty]
    public partial bool ShowRepairGuide { get; set; }

    public string RepairGuideText => "客户端文件缺失，可补全下载或前往官方页面：";

    private string? _lastLaunchVersionId;

    /// <summary>跳版本页并选中该版本（补全下载）</summary>
    [RelayCommand]
    private void GoRepair() => MainViewModel.Current?.NavigateToVersion(_lastLaunchVersionId);

    /// <summary>打开官方下载页（minecraft.net）</summary>
    [RelayCommand]
    private void OpenOfficialDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.minecraft.net/zh-hans/download") { UseShellExecute = true });
        }
        catch { /* 无法打开浏览器忽略 */ }
    }

    /// <summary>启动配置摘要（内存/Java/隔离，显示在启动区小字）</summary>
    [ObservableProperty]
    public partial string LaunchConfigText { get; set; } = "";

    /// <summary>从版本页请求启动（自动选中版本并走 Launch 流程）</summary>
    public async Task RequestLaunchAsync(string versionId, string gameDir)
    {
        await RefreshVersionsAsync();
        var found = InstalledVersions.FirstOrDefault(v => v.Name.Equals(versionId, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            InstalledVersions.Add(new VersionInstanceVM(versionId, "本启动器", gameDir));
            found = InstalledVersions[^1];
        }
        SelectedVersion = found;
        await LaunchAsync();
    }

    /// <summary>刷新配置摘要（启动区小字；设置页改动后切回主页即更新）</summary>
    public void RefreshConfigText()
    {
        var s = LauncherSettings.Current;
        var mem = s.MemoryMb > 0 ? $"{s.MemoryMb / 1024.0:0.#}G" : "总内存 60%";
        var java = string.IsNullOrWhiteSpace(s.JavaPath) ? "自动" : Path.GetFileName(s.JavaPath);
        var iso = s.VersionIsolation ? "隔离" : "共享";
        LaunchConfigText = $"内存 {mem} · Java {java} · 版本{iso}";
    }

    [ObservableProperty]
    public partial bool IsLaunching { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial double LaunchProgress { get; set; }

    [ObservableProperty]
    public partial int CurrentStageIndex { get; set; } = -1;

    [ObservableProperty]
    public partial Bitmap? PlayerAvatar { get; set; }

    [ObservableProperty]
    public partial string PlayerName { get; set; } = "未登录";

    /// <summary>账号类型徽章（正版/离线/未登录）——账号页已融合进主页，头像 Popup 承载管理</summary>
    [ObservableProperty]
    public partial string AccountTypeText { get; set; } = "未登录";

    /// <summary>账号管理（登录/切换/删除，头像 Popup 面板承载）</summary>
    public AccountViewModel Account { get; } = new();

    public ObservableCollection<string> GameLogs { get; } = [];

    /// <summary>启动记录（跨会话，可回看失败原因）</summary>
    public ObservableCollection<LaunchHistoryEntry> LaunchHistory { get; } = [];

    // ---------- 日志卡 Tab（控制台 / 启动记录） ----------

    [ObservableProperty]
    public partial bool IsConsoleTabSelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsHistoryTabSelected { get; set; }

    /// <summary>控制台是否有日志（空状态显示用）</summary>
    [ObservableProperty]
    public partial bool HasLogs { get; set; }

    /// <summary>是否有启动记录（空状态显示用）</summary>
    [ObservableProperty]
    public partial bool HasHistory { get; set; }

    /// <summary>启动记录条数（Tab 计数徽章）</summary>
    [ObservableProperty]
    public partial int HistoryCount { get; set; }

    [RelayCommand]
    private void SwitchLogTab(string tab)
    {
        IsConsoleTabSelected = tab == "console";
        IsHistoryTabSelected = tab == "history";
    }

    private System.Diagnostics.Stopwatch? _launchWatch;

    public HomeViewModel()
    {
        foreach (var name in StageNames) Stages.Add(new LaunchStageVM(name));
        // 账号状态实时同步：账号页登录/切换/退出后主页玩家区立即刷新
        _accounts.Changed += RefreshPlayer;
        LaunchHistoryService.Changed += ReloadLaunchHistory;
        ReloadLaunchHistory();
    }

    private void ReloadLaunchHistory()
    {
        LaunchHistory.Clear();
        foreach (var h in LaunchHistoryService.All) LaunchHistory.Add(h);
        HistoryCount = LaunchHistory.Count;
        HasHistory = LaunchHistory.Count > 0;
    }

    [RelayCommand]
    private async Task ClearLaunchHistory()
    {
        var owner = DialogService.MainWindow();
        if (owner is null || !await DialogService.Confirm(owner, "清除全部启动记录？", "清除记录", "清除", "取消"))
            return;
        LaunchHistoryService.Clear();
    }

    public async Task InitializeAsync()
    {
        _accounts.Load();
        RefreshPlayer();
        RefreshConfigText();
        await RefreshVersionsAsync();
    }

    /// <summary>
    /// 刷新已安装版本列表（下载/安装完成后切回主页时调用——列表不能停留在启动时的快照）。
    /// 跨所有扫描源（自建目录 + PCL/官方等已有环境）；来源标签按版本判定：
    /// 本启动器安装的标"本启动器"（.yanla-installed 标记）；否则标所在目录来源（PCL2 / 官方 / 自配）。
    /// </summary>
    public async Task RefreshVersionsAsync()
    {
        try
        {
            var svc = new VersionManifestService();
            await svc.RefreshAsync();
            InstalledVersions.Clear();
            foreach (var e in svc.Entries.Where(e => e.Installed))
                InstalledVersions.Add(new VersionInstanceVM(e.Id, LabelFor(e.Id, e.GameDirectory), e.GameDirectory));
            // 目录扫描补漏：加载器版本（fabric/forge/neoforge/quilt 等不在 Mojang manifest）
            foreach (var (dir, _) in GameDirectory.ScanSourceDirs())
            {
                var versionsDir = Path.Combine(dir, "versions");
                if (!Directory.Exists(versionsDir)) continue;
                foreach (var d in Directory.EnumerateDirectories(versionsDir))
                {
                    var id = Path.GetFileName(d);
                    if (InstalledVersions.Any(v => v.Name.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                    if (File.Exists(Path.Combine(d, $"{id}.json")))
                        InstalledVersions.Add(new VersionInstanceVM(id, LabelFor(id, dir), dir));
                }
            }
            if (InstalledVersions.Count > 0 && SelectedVersion is null)
                SelectedVersion = InstalledVersions[0];
        }
        catch { }
    }

    /// <summary>版本标签：本启动器安装 → "本启动器"；否则所在目录来源（PCL2 扫描/官方/自配）</summary>
    private static string LabelFor(string id, string gameDir)
        => InstallMarker.IsMarked(gameDir, id) ? "本启动器" : GameDirectory.SourceLabel(GameDirectory.SourceOf(gameDir));

    private void RefreshPlayer()
    {
        var acc = _accounts.Current;
        PlayerName = acc?.Name ?? "未登录";
        AccountTypeText = acc?.Type == "microsoft" ? "正版" : acc?.Type == "offline" ? "离线" : "未登录";
        PlayerAvatar = null;
        if (acc is null) return;

        // 本地皮肤优先（点击头像更换）；否则 minotar 网络头像
        var skinPath = LocalSkinPath(acc.Name);
        if (File.Exists(skinPath))
        {
            try { PlayerAvatar = new Avalonia.Media.Imaging.Bitmap(skinPath); return; }
            catch { /* 损坏皮肤回退网络 */ }
        }
        _ = ImageLoader.LoadAsync($"https://minotar.net/helm/{Uri.EscapeDataString(acc.Name)}/64.png",
            bmp => PlayerAvatar = bmp);
    }

    /// <summary>本地皮肤路径（AppData\Launcher\skins\{name}.png）</summary>
    private static string LocalSkinPath(string name)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Launcher", "skins", $"{name}.png");

    /// <summary>更换皮肤：复制本地图片为头像（离线模式皮肤仅启动器显示，游戏内不生效——Minecraft 限制）</summary>
    public void ApplyLocalSkin(string sourcePath)
    {
        var acc = _accounts.Current;
        if (acc is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LocalSkinPath(acc.Name))!);
            File.Copy(sourcePath, LocalSkinPath(acc.Name), overwrite: true);
            RefreshPlayer();
            NotificationService.Success("已更换皮肤（游戏内不生效，离线模式限制）");
        }
        catch (Exception ex)
        {
            NotificationService.Error($"换肤失败: {ex.Message}");
        }
    }

    /// <summary>推进阶段指示条</summary>
    private void SetStage(string stageName)
    {
        var idx = Array.IndexOf(StageNames, stageName);
        if (idx < 0) return;
        CurrentStageIndex = idx;
        for (var i = 0; i < Stages.Count; i++)
        {
            Stages[i].IsDone = i < idx;
            Stages[i].IsCurrent = i == idx;
        }
        // 阶段进度映射：前 4 个阶段占 0-80%，游戏加载 80-100%
        LaunchProgress = idx switch
        {
            0 => 15,
            1 => 35,
            2 => 55,
            3 => 75,
            4 => 85,
            _ => LaunchProgress,
        };
        LaunchStatus = stageName == "启动 JVM" ? "正在启动 JVM…" : $"{stageName}…";
    }

    [RelayCommand]
    private Task Launch() => LaunchAsync();

    /// <summary>启动核心（主页按钮与版本页 [启动] 共用）</summary>
    private async Task LaunchAsync()
    {
        if (IsLaunching || IsRunning) return;
        var version = SelectedVersion;
        if (version is null) { LaunchStatus = "请先选择版本"; return; }
        _lastLaunchVersionId = version.Name;
        ShowRepairGuide = false; // 清除上次失败的修复入口
        var account = _accounts.Current;
        if (account is null) { LaunchStatus = "请先在【账号】页登录"; return; }

        GameLogs.Clear();
        HasLogs = false;
        IsLaunching = true;
        LaunchProgress = 0;
        CurrentStageIndex = -1;
        foreach (var s in Stages) { s.IsDone = false; s.IsCurrent = false; }
        LaunchState = "准备中";
        LaunchStatus = $"正在准备 {version.Name}…";
        _launchWatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 启动链路（后台线程；阶段回调切回 UI 更新指示条）——内存/Java/参数：版本级配置覆盖全局
            var gameDir = version.GameDir.Length > 0 ? version.GameDir : GameDirectory.Detect();
            var s = LauncherSettings.Current;
            var (memCfg, javaCfg, argsCfg) = VersionConfigService.Merge(gameDir, version.Name, s);
            var memMb = memCfg > 0
                ? memCfg
                : (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 * 0.6);
            var extraArgs = argsCfg?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!string.IsNullOrEmpty(javaCfg))
                s.JavaPath = javaCfg; // 版本级 Java 优先（GameLaunchService 读 LauncherSettings）
            // 正版账号：启动前静默刷新 access token（过期自动换新，用户无感；刷新失败提示重新登录）
            var accessToken = "token";
            if (account.Type == "microsoft")
            {
                try
                {
                    var session = await Task.Run(() => _accounts.RefreshMicrosoftAsync());
                    accessToken = session.AccessToken;
                }
                catch (Exception ex)
                {
                    LaunchStatus = $"正版登录已失效：{ex.Message}（请到账号页重新登录）";
                    LaunchState = "失败";
                    IsLaunching = false;
                    return;
                }
            }
            _running = await Task.Run(() => _launcher.LaunchAsync(
                version.Name, gameDir, account.Name, account.Uuid, accessToken,
                memoryMb: memMb, extraJvmArgs: extraArgs,
                onLog: AppendLog, onStage: st => Dispatcher.UIThread.Post(() => SetStage(st)),
                ct: CancellationToken.None));

            // 游戏进程已启动（窗口拉起）
            IsLaunching = false;
            IsRunning = true;
            LaunchState = "运行中";
            LaunchProgress = 100;
            LaunchStatus = $"游戏运行中（{account.Name}）· 点击停止可结束";
            SetStage("运行中");
            NotificationService.Success("游戏窗口已拉起");

            // 等待退出
            await Task.Run(() => _running.Process.WaitForExit());
            var code = LaunchProcess.GetExitCode(_running);
            AppendLog($"§ 游戏进程已退出（exitStatus={code}）");
            if (_userStopped)
            {
                LaunchState = "已退出";
                LaunchStatus = "已停止游戏";
                LaunchHistoryService.Record(version.Name, LaunchOutcome.Stopped, null, _launchWatch?.Elapsed.TotalSeconds ?? 0);
            }
            else if (code == 0)
            {
                LaunchState = "已退出";
                LaunchStatus = "游戏正常退出";
                LaunchHistoryService.Record(version.Name, LaunchOutcome.Success, null, _launchWatch?.Elapsed.TotalSeconds ?? 0);
            }
            else
            {
                LaunchState = $"异常退出（{code}）";
                LaunchStatus = "游戏异常退出，请查看日志";
                LaunchHistoryService.Record(version.Name, LaunchOutcome.Crashed, $"退出码 {code}", _launchWatch?.Elapsed.TotalSeconds ?? 0);
                // 崩溃弹窗（PCL 式）：游戏日志尾部 + 导出报告
                var logTail = string.Join(Environment.NewLine, GameLogs.TakeLast(40));
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    Views.CrashReportWindow.Show($"游戏崩溃退出（退出码 {code}）",
                        $"版本 {version.Name} 异常退出，退出码 {code}。" + Environment.NewLine
                        + Environment.NewLine + "最近日志：" + Environment.NewLine + logTail,
                        logTail));
            }
            IsRunning = false;
            _running = null;
            CurrentStageIndex = -1;
            GameLogs.Clear(); // 退出后自动清空控制台（启动记录/日志文件保留本次错误）
            HasLogs = false;
        }
        catch (Exception ex)
        {
            LaunchState = "失败";
            // 客户端文件缺失（残件版本）：显示修复入口按钮
            ShowRepairGuide = ex is FileNotFoundException;
            LaunchStatus = ShowRepairGuide
                ? "客户端文件缺失，无法启动（可补全下载或前往官方页面）"
                : ex.Message;
            AppendLog($"§ 启动失败: {ex.Message}");
            LaunchHistoryService.Record(version.Name, LaunchOutcome.Failed, ex.Message, _launchWatch?.Elapsed.TotalSeconds ?? 0);
            IsLaunching = false;
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void StopGame()
    {
        _userStopped = true;
        try { _running?.Process.Kill(); } catch { }
        AppendLog("§ 已请求停止游戏");
    }

    private void AppendLog(string line)
    {
        // 进程输出事件来自后台线程，切回 UI 线程操作集合
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(line));
            return;
        }
        if (GameLogs.Count >= MaxLogLines) GameLogs.RemoveAt(0);
        GameLogs.Add(line);
        HasLogs = true;
        AppendToLaunchLog(line);
    }

    /// <summary>控制台同步落盘（AppData\Launcher\logs\launch-*.log）——启动报错可回看</summary>
    private void AppendToLaunchLog(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"launch-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch { }
    }
}

/// <summary>启动阶段指示项</summary>
public partial class LaunchStageVM : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    public partial bool IsDone { get; set; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>指示点颜色：完成=暗青、当前=主强调、未到=灰（单一强调色系）</summary>
    public IBrush DotColor => IsDone ? new SolidColorBrush(Color.Parse("#1E8F82"))
        : IsCurrent ? new SolidColorBrush(Color.Parse("#2DD4BF"))
        : new SolidColorBrush(Color.Parse("#3A4250"));

    public LaunchStageVM(string name) => Name = name;

    partial void OnIsDoneChanged(bool value) => OnPropertyChanged(nameof(DotColor));
    partial void OnIsCurrentChanged(bool value) => OnPropertyChanged(nameof(DotColor));
}
