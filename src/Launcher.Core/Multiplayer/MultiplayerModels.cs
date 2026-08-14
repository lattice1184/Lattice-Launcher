using System.Text.Json.Serialization;

namespace Launcher.Core.Multiplayer;

/// <summary>联机会话状态（UI 驱动）——8-14 从 Terracotta* 通用化（EasyTier 第二联机共用）</summary>
public enum MultiplayerSessionState
{
    Idle,      // 无会话
    Creating,  // 房主：扫描局域网世界/启动中
    Joining,   // 客机：连接中
    Active,    // 房间已就绪
    Stopping,  // 收尾中
}

/// <summary>联机会话快照（房间码 + 玩家列表）——所有联机方案统一形状</summary>
public sealed record MultiplayerSnapshot(
    string? RoomCode,
    MultiplayerSessionState State,
    IReadOnlyList<MultiplayerPlayer> Players);

/// <summary>房间内玩家</summary>
public sealed record MultiplayerPlayer(
    string Name,
    string MachineId,
    bool IsHost,
    bool IsLocal,
    int? LatencyMs);

/// <summary>会话停止原因（Stopped 事件）</summary>
public enum MultiplayerStopReason
{
    Manual,             // 用户主动离开
    BackendExited,      // 联机后端进程退出（异常）
    WorldClosed,        // 局域网世界关闭
    ServiceFailed,      // 其他协议异常
}

/// <summary>联机失败类型（UI 文案映射）</summary>
public enum MultiplayerLobbyFailure
{
    InvalidRoomCode,        // 房间码无效
    WorldUnavailable,       // 没检测到局域网世界
    BackendUnavailable,     // 联机模块不可用
    BackendBusy,            // 后端被其他进程占用
    ProtocolFailed,         // 协议/状态异常
    StartupFailed,          // 启动失败
    RoomConnectionFailed,   // 连不上房主
    NetworkFailed,          // 网络层失败（组网未建立等）
    Cancelled,              // 用户取消
}

/// <summary>联机操作异常（带失败类型，UI 直接映射文案）</summary>
public sealed class MultiplayerLobbyException : Exception
{
    public MultiplayerLobbyFailure Failure { get; }

    public MultiplayerLobbyException(MultiplayerLobbyFailure failure, string message, Exception? inner = null)
        : base(message, inner)
        => Failure = failure;
}

/// <summary>联机方案标识（MultiplayerViewModel 方案选择）</summary>
public enum MultiplayerBackend
{
    Terracotta, // 陶瓦联机（现有）
    EasyTier,   // EasyTier 虚拟组网（8-14 新增）
}

/// <summary>联机后端服务统一契约：任何方案（陶瓦/EasyTier/未来）实现此接口——VM 只依赖接口。</summary>
public interface IMultiplayerLobbyService : IDisposable
{
    /// <summary>房间状态变化（UI 订阅后切线程应用）</summary>
    event Action<MultiplayerSnapshot>? SnapshotChanged;

    /// <summary>异常停止（用户主动离开走 Manual）</summary>
    event Action<MultiplayerStopReason>? Stopped;

    /// <summary>当前快照</summary>
    MultiplayerSnapshot? Current { get; }

    /// <summary>房主建房间（陶瓦：扫描局域网世界；EasyTier：启动组网节点）</summary>
    Task<MultiplayerSnapshot> CreateHostAsync(string playerName, CancellationToken ct);

    /// <summary>客机凭房间码加入</summary>
    Task<MultiplayerSnapshot> JoinAsync(string roomCode, string playerName, CancellationToken ct);

    /// <summary>离开房间 / 停止后端</summary>
    Task StopAsync(CancellationToken ct);
}
