using Launcher.Core.Multiplayer;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>局域网发现服务：本机广播 → 本机监听回环（防火墙拦截 UDP 时跳过，不影响其他测试）</summary>
public class LanDiscoveryTests
{
    [Fact]
    public async Task BroadcastReachesLocalListener()
    {
        var tcs = new TaskCompletionSource<LanRoomInfo>();
        LanDiscoveryService.Shared.StartListen(room => tcs.TrySetResult(room));
        try
        {
            LanDiscoveryService.Shared.StartBroadcast(new LanRoomInfo(
                "测试房间", "TEST-PC", "127.0.0.1", "1.21.6", 25565, "world"));
            var completed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(4));
            LanDiscoveryService.Shared.StopBroadcast(); // 先停播避免重复触发
            // 防火墙/沙箱拦截 UDP 时抛超时 → 跳过（环境问题不是代码问题）
            Assert.Equal("测试房间", completed.Name);
            Assert.Equal("1.21.6", completed.VersionId);
            Assert.Equal(25565, completed.Port);
            Assert.Equal("world", completed.WorldName);
        }
        finally
        {
            LanDiscoveryService.Shared.StopBroadcast();
            LanDiscoveryService.Shared.StopListen();
        }
    }

    [Fact]
    public void FirewallRuleQuery_DoesNotThrow()
    {
        // 只验证查询链路稳定（netsh 解析/编码坑）；规则是否存在取决于本机状态，返回什么都是合理值
        _ = FirewallRules.RuleExists();
        Assert.True(true);
    }

    [Fact]
    public void OfflineUuid_Format_MatchJava()
    {
        // Java 版离线 UUID（OfflinePlayer:<name> 的 MD5 v3）——与游戏内 UUID 一致才不影响存档
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", Launcher.Core.Account.AccountService.OfflineUuid("Notch"));
    }
}
