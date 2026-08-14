using Launcher.Core.Multiplayer;

namespace Launcher.Core.Tests;

/// <summary>EasyTier 联机（8-14 第二方案）：虚拟 IP 分配与房间码解析（纯函数离线可测）</summary>
public class EasyTierLobbyServiceTests
{
    [Fact]
    public void AssignVirtualIp_SameRoomDifferentPlayers_DifferentIps()
    {
        // 同房间（网络名相同）两个玩家 → 不同虚拟 IP（防 IP 冲突）
        var a = EasyTierLobbyService.AssignVirtualIp("mc-山海-1234", "Alice");
        var b = EasyTierLobbyService.AssignVirtualIp("mc-山海-1234", "Bob");
        Assert.NotEqual(a, b);
        Assert.StartsWith("10.144.144.", a);
        Assert.StartsWith("10.144.144.", b);
    }

    [Fact]
    public void AssignVirtualIp_DifferentRooms_AnyIp()
    {
        // 不同房间 → 各自 IP 合法（不要求互异——房间间不互通）
        var a = EasyTierLobbyService.AssignVirtualIp("mc-山-1", "Alice");
        Assert.StartsWith("10.144.144.", a);
        var last = int.Parse(a["10.144.144.".Length..]);
        Assert.InRange(last, 2, 254);
    }

    [Fact]
    public void AssignVirtualIp_SameInput_Deterministic()
    {
        Assert.Equal(
            EasyTierLobbyService.AssignVirtualIp("net", "player"),
            EasyTierLobbyService.AssignVirtualIp("net", "player"));
    }
}
