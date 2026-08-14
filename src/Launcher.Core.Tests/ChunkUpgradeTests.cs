using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>8-16 动态升片判定（渐进限速源掉速自动加连接——OBS/GitHub 大文件治本）</summary>
public class ChunkUpgradeTests
{
    [Fact]
    public void Upgrade_SlowSpeed_MidDownload_HasHeadroom()
    {
        // 3s 均速 100KB/s（远低于 300KB/s 阈值）、完成 40%、4 片 → 8 片（max 16）
        Assert.True(DownloadService.ShouldUpgradeChunks(100 * 1024, 40_000_000, 100_000_000, 4, 16, 30));
    }

    [Fact]
    public void Upgrade_FastSpeed_No()
    {
        Assert.False(DownloadService.ShouldUpgradeChunks(5 * 1024 * 1024, 40_000_000, 100_000_000, 4, 16, 30));
    }

    [Fact]
    public void Upgrade_NearCompletion_No()
    {
        // 剩余 < 8MB（完成 95%）——升片重下损失大于收益
        Assert.False(DownloadService.ShouldUpgradeChunks(100 * 1024, 95_000_000, 100_000_000, 4, 16, 30));
    }

    [Fact]
    public void Upgrade_LateStageStillYes()
    {
        // 8-17 用户实测：OBS 后期 80%+ 才掉速——「完成 <80%」会挡住后期升片；剩余 ≥8MB 就该升
        Assert.True(DownloadService.ShouldUpgradeChunks(100 * 1024, 90_000_000, 100_000_000, 4, 16, 30));
        // 边界：剩余恰好 8 MiB（8,388,608 字节）→ 升（≥）
        Assert.True(DownloadService.ShouldUpgradeChunks(100 * 1024, 100_000_000 - 8 * 1024 * 1024, 100_000_000, 4, 16, 30));
    }

    [Fact]
    public void Upgrade_AtMaxChunks_No()
    {
        Assert.False(DownloadService.ShouldUpgradeChunks(100 * 1024, 40_000_000, 100_000_000, 16, 16, 30));
    }

    [Fact]
    public void Upgrade_WithinCooldown_No()
    {
        // 距上次升片 3s（<10s 冷却）——防抖动循环
        Assert.False(DownloadService.ShouldUpgradeChunks(100 * 1024, 40_000_000, 100_000_000, 4, 16, 3));
    }
}
