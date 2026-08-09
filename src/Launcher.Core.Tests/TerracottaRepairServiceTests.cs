using Launcher.Core.Multiplayer;

namespace Launcher.Core.Tests;

/// <summary>联机一键修复（AL44）：残留进程清理（不真杀进程——无 terracotta 进程场景）+ 锁文件处理</summary>
public class TerracottaRepairServiceTests
{
    [Fact]
    public void KillStaleInstances_NoProcess_ReturnsZero()
    {
        // 测试环境无 terracotta 进程（测试不启动真模块）→ 击杀 0，不抛
        var killed = TerracottaRepairService.KillStaleInstances();
        Assert.Equal(0, killed);
    }

    [Fact]
    public void KillStaleInstances_MissingLockFile_DoesNotThrow()
    {
        // 锁文件不存在（或不可删）时静默——清理函数不得成为新故障源
        try { File.Delete(TerracottaRepairService.LockPath); } catch { }
        var killed = TerracottaRepairService.KillStaleInstances();
        Assert.True(killed >= 0);
    }
}
