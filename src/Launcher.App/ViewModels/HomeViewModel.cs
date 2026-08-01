using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Account;
using Launcher.Core.Launch;
using Launcher.Core.Services;
using Launcher.Core.Utils;

namespace Launcher.App.ViewModels;

/// <summary>
/// 主页：版本选择 + 启动状态机（就绪→准备中→运行中→已退出/失败）+ 游戏控制台。
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly GameLaunchService _launcher = new();
    private readonly AccountService _accounts = new();
    private LaunchProcess.LaunchResult? _running;
    private const int MaxLogLines = 500;

    public ObservableCollection<VersionInstanceVM> InstalledVersions { get; } = [];

    [ObservableProperty]
    public partial VersionInstanceVM? SelectedVersion { get; set; }

    [ObservableProperty]
    public partial string LaunchState { get; set; } = "就绪";

    [ObservableProperty]
    public partial string LaunchStatus { get; set; } = "选择版本并启动";

    [ObservableProperty]
    public partial bool IsLaunching { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial double LaunchProgress { get; set; }

    public ObservableCollection<string> GameLogs { get; } = [];

    public async Task InitializeAsync()
    {
        _accounts.Load();
        try
        {
            var svc = new VersionManifestService();
            await svc.RefreshAsync();
            InstalledVersions.Clear();
            foreach (var e in svc.Entries.Where(e => e.Installed))
                InstalledVersions.Add(new VersionInstanceVM(e.Id));
            if (InstalledVersions.Count > 0) SelectedVersion = InstalledVersions[0];
        }
        catch { }
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
        LaunchState = "准备中";
        LaunchStatus = $"正在准备 {version.Name}…";

        try
        {
            // 阶段 1：构建启动档案（20%）
            _running = await Task.Run(() => _launcher.LaunchAsync(
                version.Name, GameDirectory.Detect(), account.Name, account.Uuid,
                memoryMb: 4096, extraJvmArgs: null,
                onLog: AppendLog), CancellationToken.None);
            LaunchProgress = 30;

            // 阶段 2：游戏进程已启动（30%）
            IsLaunching = false;
            IsRunning = true;
            LaunchState = "运行中";
            LaunchStatus = $"游戏运行中（{account.Name}）· 点击停止可结束";
            LaunchProgress = 100;

            // 阶段 3：等待退出
            await Task.Run(() => _running.Process.WaitForExit());
            var code = LaunchProcess.GetExitCode(_running);
            AppendLog($"§ 游戏进程已退出（exitStatus={code}）");
            LaunchState = code == 0 ? "已退出" : $"异常退出（{code}）";
            LaunchStatus = code == 0 ? "游戏正常退出" : "游戏异常退出，请查看日志";
            IsRunning = false;
            _running = null;
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
