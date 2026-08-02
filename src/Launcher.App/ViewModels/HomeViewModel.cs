using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Account;
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
    private readonly AccountService _accounts = new();
    private LaunchProcess.LaunchResult? _running;
    private const int MaxLogLines = 500;

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

    public ObservableCollection<string> GameLogs { get; } = [];

    public HomeViewModel()
    {
        foreach (var name in StageNames) Stages.Add(new LaunchStageVM(name));
    }

    public async Task InitializeAsync()
    {
        _accounts.Load();
        RefreshPlayer();
        await RefreshVersionsAsync();
    }

    /// <summary>
    /// 刷新已安装版本列表（下载/安装完成后切回主页时调用——列表不能停留在启动时的快照）。
    /// 来源标签：当前游戏目录来自 PCL2 / 本启动器 / 官方 / 自配。
    /// </summary>
    public async Task RefreshVersionsAsync()
    {
        try
        {
            var svc = new VersionManifestService();
            await svc.RefreshAsync();
            var sourceLabel = GameDirectory.SourceLabel(GameDirectory.DetectSource());
            InstalledVersions.Clear();
            foreach (var e in svc.Entries.Where(e => e.Installed))
                InstalledVersions.Add(new VersionInstanceVM(e.Id, sourceLabel));
            // 目录扫描：加载器版本（fabric/forge/neoforge/quilt 等不在 Mojang manifest）
            var versionsDir = Path.Combine(GameDirectory.Detect(), "versions");
            if (Directory.Exists(versionsDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(versionsDir))
                {
                    var id = Path.GetFileName(dir);
                    if (InstalledVersions.Any(v => v.Name.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                    if (File.Exists(Path.Combine(dir, $"{id}.json")))
                        InstalledVersions.Add(new VersionInstanceVM(id, sourceLabel));
                }
            }
            if (InstalledVersions.Count > 0 && SelectedVersion is null)
                SelectedVersion = InstalledVersions[0];
        }
        catch { }
    }

    private void RefreshPlayer()
    {
        var acc = _accounts.Current;
        PlayerName = acc?.Name ?? "未登录";
        PlayerAvatar = null;
        if (acc is not null)
            _ = ImageLoader.LoadAsync($"https://minotar.net/helm/{Uri.EscapeDataString(acc.Name)}/64.png",
                bmp => PlayerAvatar = bmp);
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
    private async Task Launch()
    {
        if (IsLaunching || IsRunning) return;
        var version = SelectedVersion;
        if (version is null) { LaunchStatus = "请先选择版本"; return; }
        var account = _accounts.Current;
        if (account is null) { LaunchStatus = "请先在【账号】页登录（离线或正版）"; return; }

        GameLogs.Clear();
        IsLaunching = true;
        LaunchProgress = 0;
        CurrentStageIndex = -1;
        foreach (var s in Stages) { s.IsDone = false; s.IsCurrent = false; }
        LaunchState = "准备中";
        LaunchStatus = $"正在准备 {version.Name}…";

        try
        {
            // 启动链路（后台线程；阶段回调切回 UI 更新指示条）
            _running = await Task.Run(() => _launcher.LaunchAsync(
                version.Name, GameDirectory.Detect(), account.Name, account.Uuid,
                memoryMb: 4096, extraJvmArgs: null,
                onLog: AppendLog, onStage: s => Dispatcher.UIThread.Post(() => SetStage(s)),
                ct: CancellationToken.None));

            // 游戏进程已启动
            IsLaunching = false;
            IsRunning = true;
            LaunchState = "运行中";
            LaunchProgress = 100;
            LaunchStatus = $"游戏运行中（{account.Name}）· 点击停止可结束";
            SetStage("运行中");

            // 等待退出
            await Task.Run(() => _running.Process.WaitForExit());
            var code = LaunchProcess.GetExitCode(_running);
            AppendLog($"§ 游戏进程已退出（exitStatus={code}）");
            LaunchState = code == 0 ? "已退出" : $"异常退出（{code}）";
            LaunchStatus = code == 0 ? "游戏正常退出" : "游戏异常退出，请查看日志";
            IsRunning = false;
            _running = null;
            CurrentStageIndex = -1;
        }
        catch (Exception ex)
        {
            LaunchState = "失败";
            LaunchStatus = ex.Message;
            AppendLog($"§ 启动失败: {ex.Message}");
            IsLaunching = false;
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void StopGame()
    {
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
