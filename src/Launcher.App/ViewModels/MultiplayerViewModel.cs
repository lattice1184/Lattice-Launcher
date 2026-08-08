using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.App.Views;
using Launcher.Core.Multiplayer;
using Launcher.Core.Utils;

namespace Launcher.App.ViewModels;

/// <summary>局域网房间行（展示便捷属性）</summary>
public sealed record LanRoomVM(LanRoomInfo Room)
{
    public string Name => Room.Name;
    public string VersionId => Room.VersionId;
    public string Host => $"{Room.HostName} · {Room.Ip}:{Room.Port}";
    public string World => $"世界：{Room.WorldName}";
}

/// <summary>
/// 联机页：监听局域网房间广播，列表实时刷新（6s 未心跳视为离线）。
/// 开房间 = 开服页启动服务端（自动广播）；加入 = 复用主页一键进服链路（--server IP --port）。
/// </summary>
public partial class MultiplayerViewModel : ViewModelBase
{
    private const int TimeoutSeconds = 6;

    private readonly Dictionary<string, DateTime> _lastSeen = [];
    private readonly DispatcherTimer _ticker;

    public ObservableCollection<LanRoomVM> Rooms { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "正在扫描局域网…";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    /// <summary>防火墙规则缺失（联机页打开时检测；缺失则显示提示条+放行按钮）</summary>
    [ObservableProperty]
    public partial bool IsFwMissing { get; set; }

    public MultiplayerViewModel()
    {
        LanDiscoveryService.Shared.StartListen(OnRoomReceived);
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => Prune();
        _ticker.Start();
        // 防火墙检测较慢（netsh 查询）——后台跑，结果回 UI 线程
        _ = Task.Run(() =>
        {
            var missing = !FirewallRules.RuleExists();
            Dispatcher.UIThread.Post(() => IsFwMissing = missing);
        });
    }

    /// <summary>一键放行防火墙（UAC 提权一次；失败给手动步骤）</summary>
    [RelayCommand]
    private void AllowFirewall()
    {
        if (FirewallRules.TryAddRule())
        {
            IsFwMissing = false;
            NotificationService.Success("防火墙已放行 UDP 34198，联机房间可被发现");
        }
        else
        {
            NotificationService.Error("未放行（已取消或失败）。" + Environment.NewLine + FirewallRules.ManualHint());
        }
    }

    /// <summary>UDP 回调在后台线程——切回 UI 线程更新集合</summary>
    private void OnRoomReceived(LanRoomInfo room)
    {
        if (Dispatcher.UIThread.CheckAccess()) UpdateRoom(room);
        else Dispatcher.UIThread.Post(() => UpdateRoom(room));
    }

    private void UpdateRoom(LanRoomInfo room)
    {
        var key = $"{room.Ip}:{room.Port}";
        _lastSeen[key] = DateTime.Now;
        for (var i = 0; i < Rooms.Count; i++)
        {
            if ($"{Rooms[i].Room.Ip}:{Rooms[i].Room.Port}" != key) continue;
            if (!Rooms[i].Room.Equals(room)) Rooms[i] = new LanRoomVM(room); // 信息变化才替换（避免每心跳重渲染）
            return;
        }
        Rooms.Add(new LanRoomVM(room));
        IsEmpty = false;
        Status = $"发现 {Rooms.Count} 个房间";
    }

    /// <summary>每秒清理超时房间（心跳 2s，6s 无心跳 = 主机退出/断网）</summary>
    private void Prune()
    {
        var now = DateTime.Now;
        for (var i = Rooms.Count - 1; i >= 0; i--)
        {
            var key = $"{Rooms[i].Room.Ip}:{Rooms[i].Room.Port}";
            if (!_lastSeen.TryGetValue(key, out var seen) || (now - seen).TotalSeconds <= TimeoutSeconds) continue;
            _lastSeen.Remove(key);
            Rooms.RemoveAt(i);
        }
        IsEmpty = Rooms.Count == 0;
        if (IsEmpty) Status = "正在扫描局域网…";
        else if (Rooms.Count > 0) Status = $"发现 {Rooms.Count} 个房间";
    }

    /// <summary>创建房间：选版本/房间名/端口 → 防火墙放行 → 启动服务端（自动广播，本机回环可见自己的房间）</summary>
    [RelayCommand]
    private async Task CreateRoom()
    {
        if (MainViewModel.Current is not { } main) return;
        if (main.Server.IsRunning) { Status = "服务端已在运行中，同一时间只能开一个房间"; return; }
        if (DialogService.MainWindow() is not { } owner) return;

        // 只列服务端 jar 就绪的版本——缺文件的版本选了会弹下载确认，直接过滤掉
        var usable = main.Server.InstalledVersions.Where(v => main.Server.HasServerJar(v)).ToList();
        if (usable.Count == 0)
        {
            NotificationService.Error("没有可开服的版本：先到开服页下载服务端");
            return;
        }

        var result = await new CreateRoomWindow(usable)
            .ShowDialog<CreateRoomResult?>(owner);
        if (result is null) return; // 取消

        // 防火墙放行（缺失才提权；被拒给手动步骤——不阻断创建）
        if (!FirewallRules.RuleExists() && !FirewallRules.TryAddRule())
        {
            NotificationService.Error("防火墙未放行 UDP 34198，其他电脑可能看不到房间。" + Environment.NewLine + FirewallRules.ManualHint());
        }

        var ver = main.Server.InstalledVersions.FirstOrDefault(
            v => v.Name.Equals(result.VersionId, StringComparison.OrdinalIgnoreCase));
        if (ver is null) { Status = $"未找到版本 {result.VersionId}"; return; }
        main.Server.SelectedVersion = ver;
        main.Server.ApplyRoomSettings(result.Port, result.RoomName);
        main.Server.StartServerCommand.Execute(null); // 启动后自动广播房间
        Status = $"房间「{(string.IsNullOrWhiteSpace(result.RoomName) ? "我的 " + result.VersionId + " 服务器" : result.RoomName)}」创建中…";
        NotificationService.Success("正在创建房间（服务端启动后自动广播）");
    }

    /// <summary>复制房间地址（ip:port）——发给朋友可在游戏内手动直连</summary>
    [RelayCommand]
    private async Task Copy(LanRoomVM row)
    {
        if (DialogService.MainWindow() is not { } top) return;
        var cb = Avalonia.Controls.TopLevel.GetTopLevel(top)?.Clipboard;
        if (cb is null) return;
        await cb.SetTextAsync($"{row.Room.Ip}:{row.Room.Port}");
        NotificationService.Success($"已复制 {row.Room.Ip}:{row.Room.Port}");
    }

    /// <summary>加入房间：切主页启动客户端并自动连接（复用一键进服链路）</summary>
    [RelayCommand]
    private async Task Join(LanRoomVM row)
    {
        if (MainViewModel.Current is not { } main) return;
        var dir = GameDirectory.Detect();
        main.NavigateTo("home");
        await main.Home.RequestLaunchWithServerAsync(row.Room.VersionId, dir, row.Room.Ip, row.Room.Port);
    }
}
