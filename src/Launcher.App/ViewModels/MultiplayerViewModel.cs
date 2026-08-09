using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.App.Views;
using Launcher.Core.Account;
using Launcher.Core.Diagnostics;
using Launcher.Core.Multiplayer;

namespace Launcher.App.ViewModels;

/// <summary>联机页区块（互斥显示）</summary>
public enum MultiplayerPageStep
{
    Welcome,  // 主界面：创建 / 加入卡片
    Busy,     // 创建中 / 加入中（可取消）
    Active,   // 房间已就绪
    Declined, // 未同意协议，功能不可用
}

/// <summary>房间内玩家行（展示便捷属性）</summary>
public sealed record TerracottaPlayerVM(TerracottaPlayer Player)
{
    public string Name => Player.Name;
    public string LatencyText => Player.LatencyMs is { } ms ? $"{ms} ms" : "—";
    public bool IsHost => Player.IsHost;
    public bool IsLocal => Player.IsLocal;
}

/// <summary>
/// 联机页：走陶瓦（Terracotta）联机，与开服完全分离。
/// 房主：游戏内开「局域网世界」→ 本页「创建房间」→ 出房间码；客机：输码加入。
/// 未装模块 → 弹协议窗下载；失败/停止 → 复位 + 人话文案。
/// </summary>
public partial class MultiplayerViewModel : ViewModelBase
{
    private readonly TerracottaProvisioningService _provisioning = new();
    private TerracottaLobbyService? _lobby;
    private CancellationTokenSource? _sessionCts;
    private bool _initialized;
    private bool _resetting;

    /// <summary>当前区块</summary>
    [ObservableProperty]
    public partial MultiplayerPageStep Step { get; set; } = MultiplayerPageStep.Welcome;

    [ObservableProperty]
    public partial bool IsWelcome { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsDeclined { get; set; }

    partial void OnStepChanged(MultiplayerPageStep value)
    {
        IsWelcome = value == MultiplayerPageStep.Welcome;
        IsBusy = value == MultiplayerPageStep.Busy;
        IsActive = value == MultiplayerPageStep.Active;
        IsDeclined = value == MultiplayerPageStep.Declined;
    }

    /// <summary>欢迎态 tab：默认创建房间</summary>
    [ObservableProperty]
    public partial bool IsCreateTab { get; set; } = true;

    [ObservableProperty]
    public partial bool IsJoinTab { get; set; }

    partial void OnIsCreateTabChanged(bool value) => IsJoinTab = !value;

    /// <summary>欢迎态 tab 切换（create / join）</summary>
    [RelayCommand]
    private void SwitchTab(string which) => IsCreateTab = which == "create";

    /// <summary>创建中 / 加入中的说明文字</summary>
    [ObservableProperty]
    public partial string BusyText { get; set; } = "";

    /// <summary>错误/停止原因文案（人话）</summary>
    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }


    /// <summary>房主 / 客机</summary>
    [ObservableProperty]
    public partial bool IsHost { get; set; }

    /// <summary>房间码（XXXX-XXXX）</summary>
    [ObservableProperty]
    public partial string? RoomCode { get; set; }

    /// <summary>房主名（房间标题用）</summary>
    [ObservableProperty]
    public partial string? HostName { get; set; }

    /// <summary>客机：房间码输入</summary>
    [ObservableProperty]
    public partial string JoinCode { get; set; } = "";

    /// <summary>房间内玩家</summary>
    public ObservableCollection<TerracottaPlayerVM> Players { get; } = [];

    private static string PlayerName => AccountService.Shared.Current?.Name ?? "Player";

    /// <summary>进入联机页（View Loaded）：首次检查模块，未装弹协议窗</summary>
    public async Task OnPageLoadedAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await EnsureAgreementAsync();
    }

    // ---------- 协议 ----------

    /// <summary>模块已装直接过；未装弹协议窗，不同意 → Declined 区块</summary>
    private async Task<bool> EnsureAgreementAsync()
    {
        var installed = _provisioning.TryGetAvailable();
        MultiplayerLog.Log($"协议检查: 已装模块={(installed is null ? "无" : $"v{installed.Version}")}");
        if (installed is not null) return true;
        MultiplayerLog.Log("协议检查: 弹协议窗");
        if (DialogService.MainWindow() is not { } owner) return false;
        var ok = await new TerracottaAgreementDialog(_provisioning).ShowDialog<bool>(owner);
        if (!ok)
        {
            Step = MultiplayerPageStep.Declined;
            return false;
        }
        return true;
    }

    /// <summary>Declined 区块：重新阅读并同意协议</summary>
    [RelayCommand]
    private async Task ReopenAgreement()
    {
        if (await EnsureAgreementAsync()) Step = MultiplayerPageStep.Welcome;
    }

    // ---------- 房主：创建房间 ----------

    [RelayCommand]
    private async Task CreateRoom()
    {
        if (!await EnsureAgreementAsync()) return;
        var module = _provisioning.TryGetAvailable();
        if (module is null) return;

        _lastAction = "create";
        StartSession(isHost: true, module);
        BusyText = "正在查找局域网世界…";
        try
        {
            await _lobby!.CreateHostAsync(PlayerName, _sessionCts!.Token);
        }
        catch (OperationCanceledException)
        {
            ResetAfterFailure();
        }
        catch (TerracottaLobbyException ex)
        {
            ShowFailure(ex);
        }
    }

    // ---------- 客机：加入房间 ----------

    [RelayCommand]
    private async Task JoinRoom()
    {
        var code = JoinCode.Trim();
        if (code.Length == 0)
        {
            ErrorText = "请输入房主提供的房间代码。";
            return;
        }
        if (!await EnsureAgreementAsync()) return;
        var module = _provisioning.TryGetAvailable();
        if (module is null) return;

        _lastAction = "join";
        StartSession(isHost: false, module);
        BusyText = "正在加入房间…";
        try
        {
            await _lobby!.JoinAsync(code, PlayerName, _sessionCts!.Token);
        }
        catch (OperationCanceledException)
        {
            ResetAfterFailure();
        }
        catch (TerracottaLobbyException ex)
        {
            ShowFailure(ex);
        }
    }

    /// <summary>从剪贴板粘贴房间码</summary>
    [RelayCommand]
    private async Task PasteCode()
    {
        if (DialogService.MainWindow() is not { } top) return;
        var cb = Avalonia.Controls.TopLevel.GetTopLevel(top)?.Clipboard;
        if (cb is null) return;
        var text = await cb.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            ErrorText = "剪贴板中没有房间代码。";
            return;
        }
        JoinCode = text.Trim();
    }

    // ---------- 房间内 ----------

    /// <summary>复制房间码（发给朋友用）</summary>
    [RelayCommand]
    private async Task CopyCode()
    {
        if (RoomCode is null || DialogService.MainWindow() is not { } top) return;
        var cb = Avalonia.Controls.TopLevel.GetTopLevel(top)?.Clipboard;
        if (cb is null) return;
        await cb.SetTextAsync(RoomCode);
        NotificationService.Success("已复制房间代码");
    }

    /// <summary>离开房间：确认 → 陶瓦收尾（/state/ide → /panic）→ 复位</summary>
    [RelayCommand]
    private async Task LeaveRoom()
    {
        if (_lobby is null) return;
        var (title, message, confirm) = IsHost
            ? ("退出并解散房间？", "解散后所有玩家都将断开连接。确定退出吗？", "退出")
            : ("离开房间？", "离开后将断开连接。", "离开房间");
        if (!await DialogService.Confirm(DialogService.MainWindow(), message, title, confirm, "取消")) return;

        _resetting = true; // 主动路径不发 Stopped，事件也忽略
        try
        {
            await _lobby.StopAsync(CancellationToken.None);
        }
        catch { /* 收尾失败也复位 */ }
        finally
        {
            _resetting = false;
            Reset();
        }
    }

    /// <summary>创建中 / 加入中：取消</summary>
    [RelayCommand]
    private void CancelBusy() => _sessionCts?.Cancel();

    // ---------- 会话管理 ----------

    private void StartSession(bool isHost, TerracottaModule module)
    {
        _lobby = new TerracottaLobbyService(module);
        _lobby.SnapshotChanged += OnSnapshotChanged;
        _lobby.Stopped += OnStopped;
        _sessionCts = new CancellationTokenSource();
        IsHost = isHost;
        ErrorText = null;
        RoomCode = null;
        Players.Clear();
        Step = MultiplayerPageStep.Busy;
    }

    /// <summary>Core 轮询线程回调 → 切回 UI 线程应用快照（Core 已做签名去重，只在变化时发）</summary>
    private void OnSnapshotChanged(TerracottaSnapshot snap)
    {
        if (_resetting) return;
        if (Dispatcher.UIThread.CheckAccess()) ApplySnapshot(snap);
        else Dispatcher.UIThread.Post(() => ApplySnapshot(snap));
    }

    private void ApplySnapshot(TerracottaSnapshot snap)
    {
        if (_resetting || snap.State != TerracottaSessionState.Active) return;
        Step = MultiplayerPageStep.Active;
        RoomCode = snap.RoomCode;
        HostName = snap.Players.FirstOrDefault(p => p.IsHost)?.Name ?? PlayerName;
        Players.Clear();
        foreach (var p in snap.Players) Players.Add(new TerracottaPlayerVM(p));
    }

    /// <summary>异常终止（陶瓦退出 / 世界关闭 / 服务异常）→ 复位 + 文案</summary>
    private void OnStopped(TerracottaStopReason reason)
    {
        if (_resetting) return;
        if (Dispatcher.UIThread.CheckAccess()) HandleStopped(reason);
        else Dispatcher.UIThread.Post(() => HandleStopped(reason));
    }

    private void HandleStopped(TerracottaStopReason reason)
    {
        if (_resetting) return;
        Reset();
        ErrorText = reason switch
        {
            TerracottaStopReason.TerracottaExited => "联机模块已停止，房间已解散。",
            TerracottaStopReason.MinecraftWorldClosed => "局域网世界已关闭，房间已解散。",
            TerracottaStopReason.ServiceFailed => "联机服务异常，房间已解散。",
            _ => null,
        };
    }

    /// <summary>失败复位（无文案——取消场景），随后带文案时再 set</summary>
    private void ResetAfterFailure() => Reset();

    private void ShowFailure(TerracottaLobbyException ex)
    {
        Reset();
        // AL44：统一诊断——枚举 → 人话原因+建议+修复动作（替代私有 switch，覆盖真实失败子类型）
        _lastFailure = FailureDiagnostics.ForTerracotta(ex.Failure, ex.Message);
        ErrorText = _lastFailure.Explanation;
    }

    /// <summary>最近一次失败诊断（「一键修复」依据）</summary>
    private DiagnosticHit? _lastFailure;

    /// <summary>模块版本（欢迎页展示，帮朋友双方对齐版本）</summary>
    public string ModuleVersionText
        => _provisioning.TryGetAvailable() is { } m ? $"联机模块 v{m.Version}" : "联机模块未安装";

    /// <summary>失败可一键修复（RestartService/ReinstallModule）</summary>
    public bool HasFixableError => _lastFailure?.IsAutoFixable == true && _lastFailure.Fix is FixKind.RestartService or FixKind.ReinstallModule;

    /// <summary>一键修复执行中（按钮禁用）</summary>
    [ObservableProperty]
    public partial bool IsRepairing { get; set; }

    partial void OnErrorTextChanged(string? value)
    {
        HasError = value is not null;
        OnPropertyChanged(nameof(HasFixableError));
    }

    /// <summary>
    /// AL44 一键修复：RestartService → 杀残留陶瓦进程/删锁文件；ReinstallModule → 重装模块。
    /// 完成后自动重试原动作一次（镜像启动模块「修复后自动重启一次」）；二次失败显示新诊断。
    /// </summary>
    [RelayCommand]
    private async Task RepairNow()
    {
        if (_lastFailure is not { IsAutoFixable: true }) return;
        var fix = _lastFailure.Fix;
        IsRepairing = true;
        try
        {
            if (fix == FixKind.RestartService)
            {
                TerracottaRepairService.KillStaleInstances();
            }
            else if (fix == FixKind.ReinstallModule)
            {
                await _provisioning.ReinstallAsync();
            }
            // 清错误 → 自动重试原动作一次（Snippet 记录失败来源）
            ErrorText = null;
            _lastFailure = null;
            var action = _lastAction;
            if (action == null) return;
            if (action == "join")
            {
                if (JoinCode.Length == 0) { ErrorText = "先把房主给的房间代码填进去。"; return; }
                await JoinRoom();
            }
            else
            {
                await CreateRoom();
            }
        }
        catch (Exception ex)
        {
            ErrorText = $"修复失败：{ex.Message}";
        }
        finally
        {
            IsRepairing = false;
        }
    }

    /// <summary>最近一次失败的动作来源（create/join），供一键修复后自动重试</summary>
    private string? _lastAction;

    private void Reset()
    {
        if (_lobby is not null)
        {
            _lobby.SnapshotChanged -= OnSnapshotChanged;
            _lobby.Stopped -= OnStopped;
            _lobby.Dispose();
            _lobby = null;
        }
        _sessionCts?.Dispose();
        _sessionCts = null;
        RoomCode = null;
        HostName = null;
        IsHost = false;
        Players.Clear();
        Step = MultiplayerPageStep.Welcome;
    }
}
