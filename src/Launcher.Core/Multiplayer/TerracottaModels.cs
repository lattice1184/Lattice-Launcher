using System.Text.Json.Serialization;

namespace Launcher.Core.Multiplayer;

/// <summary>已安装的陶瓦模块（terracotta.exe + 运行库 + manifest）</summary>
public sealed record TerracottaModule(string Version, string Architecture, string Directory, string ExePath);

/// <summary>陶瓦进度回显（stage：terracotta-download / terracotta-extract / terracotta-ready）</summary>
public sealed record TerracottaProvisionProgress(string Stage, int Percent);

/// <summary>联机会话状态（UI 驱动）</summary>
public enum TerracottaSessionState
{
    Idle,      // 无会话
    Creating,  // 房主：扫描局域网世界/启动中
    Joining,   // 客机：连接中
    Active,    // 房间已就绪
    Stopping,  // 收尾中
}

/// <summary>联机会话快照（房间码 + 玩家列表）</summary>
public sealed record TerracottaSnapshot(
    string? RoomCode,
    TerracottaSessionState State,
    IReadOnlyList<TerracottaPlayer> Players);

/// <summary>房间内玩家</summary>
public sealed record TerracottaPlayer(
    string Name,
    string MachineId,
    bool IsHost,
    bool IsLocal,
    int? LatencyMs);

/// <summary>会话停止原因（Stopped 事件）</summary>
public enum TerracottaStopReason
{
    Manual,             // 用户主动离开
    TerracottaExited,   // 陶瓦进程退出（异常码 3）
    MinecraftWorldClosed, // 局域网世界关闭（异常码 4）
    ServiceFailed,      // 其他协议异常
}

/// <summary>联机失败类型（UI 文案映射）</summary>
public enum TerracottaLobbyFailure
{
    InvalidRoomCode,        // 房间码无效（400）
    MinecraftWorldUnavailable, // 没检测到局域网世界（host 超时/异常码 4）
    TerracottaUnavailable,  // 模块不可用
    TerracottaBusy,         // 正被其他启动器使用
    ProtocolFailed,         // 协议/状态异常
    StartupFailed,          // 启动失败（host 异常码 3 等）
    RoomConnectionFailed,   // 连不上房主（guest 异常码 0/1/2）
    Cancelled,              // 用户取消
}

/// <summary>联机操作异常（带失败类型，UI 直接映射文案）</summary>
public sealed class TerracottaLobbyException : Exception
{
    public TerracottaLobbyFailure Failure { get; }

    public TerracottaLobbyException(TerracottaLobbyFailure failure, string message, Exception? inner = null)
        : base(message, inner)
        => Failure = failure;
}
