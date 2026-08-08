using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Launcher.Core.Multiplayer;

/// <summary>局域网房间信息（UDP 广播载体；JSON 传输，心跳 2s，6s 未刷新视为离线）</summary>
public sealed record LanRoomInfo(
    string Name,          // 房间名（motd 或主机名）
    string HostName,      // 开房主机名
    string Ip,            // 开房主机局域网 IPv4（供加入连接）
    string VersionId,     // 游戏版本（如 1.21.6）
    int Port,             // 服务器端口（server.properties server-port）
    string WorldName);    // 世界名（level-name）

/// <summary>
/// 局域网发现服务：房间主开服时 UDP 广播房间信息，联机页监听发现房间。
/// 广播 255.255.255.255:34198，同一网段互通，不依赖外网。
/// 注意：Windows 防火墙可能拦截 UDP 34198——广播/监听被拦时看不到房间，需放行（提示见联机页）。
/// </summary>
public sealed class LanDiscoveryService
{
    public const int DefaultPort = 34198;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>进程级共享实例（开服页广播 + 联机页监听共用）</summary>
    public static LanDiscoveryService Shared { get; } = new();

    private CancellationTokenSource? _bcCts;     // 广播循环
    private CancellationTokenSource? _listenCts; // 监听循环

    public bool IsBroadcasting => _bcCts is not null;

    /// <summary>开始广播房间（幂等：先停旧广播再开新广播；房间信息变化时重调即可）</summary>
    public void StartBroadcast(LanRoomInfo room)
    {
        StopBroadcast();
        _bcCts = new CancellationTokenSource();
        _ = Task.Run(() => BroadcastLoop(room, _bcCts.Token));
    }

    public void StopBroadcast()
    {
        _bcCts?.Cancel();
        _bcCts?.Dispose();
        _bcCts = null;
    }

    /// <summary>开始监听房间（幂等；onRoom 在后台线程回调，UI 层需切回主线程更新）</summary>
    public void StartListen(Action<LanRoomInfo> onRoom)
    {
        StopListen();
        _listenCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoop(onRoom, _listenCts.Token));
    }

    public void StopListen()
    {
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        _listenCts = null;
    }

    /// <summary>本机局域网 IPv4（私网段优先，过滤虚拟网卡/回环；取不到返回 127.0.0.1）</summary>
    public static string LocalIp()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ip = host.AddressList
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .FirstOrDefault(a => IsPrivate(a));
            return ip?.ToString() ?? "127.0.0.1";
        }
        catch (SocketException) { return "127.0.0.1"; }
    }

    private static bool IsPrivate(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
    }

    private static void BroadcastLoop(LanRoomInfo room, CancellationToken ct)
    {
        using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try { udp.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true); }
        catch (SocketException) { return; } // 无广播权限（沙箱等）直接退出
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(room, JsonOpts));
        var target = new IPEndPoint(IPAddress.Broadcast, DefaultPort);
        while (!ct.IsCancellationRequested)
        {
            try { udp.SendTo(payload, target); }
            catch (SocketException) { /* 网卡暂不可用，下个心跳再试 */ }
            ct.WaitHandle.WaitOne(2000); // 心跳间隔（可取消等待）
        }
    }

    private static void ListenLoop(Action<LanRoomInfo> onRoom, CancellationToken ct)
    {
        using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try { udp.Bind(new IPEndPoint(IPAddress.Any, DefaultPort)); }
        catch (SocketException) { return; } // 端口被占/无权限则静默退出
        udp.ReceiveTimeout = 1000; // 阻塞接收定时醒来检查取消
        var buffer = new byte[1024];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = udp.Receive(buffer); }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut or SocketError.WouldBlock) { continue; }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            if (n <= 0) continue;
            try
            {
                var room = JsonSerializer.Deserialize<LanRoomInfo>(Encoding.UTF8.GetString(buffer, 0, n), JsonOpts);
                if (room is not null && !string.IsNullOrWhiteSpace(room.VersionId) && room.Port > 0)
                    onRoom(room);
            }
            catch (JsonException) { /* 非本协议包（其他应用占用端口）忽略 */ }
        }
    }
}
