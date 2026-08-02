using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>下载源质量统计：记速 / 排序 / 失败降权</summary>
public class SourceStatsTests
{
    [Fact]
    public void Rank_ByAverageSpeed_FastestFirst()
    {
        var stats = new SourceStats();
        // 官方慢（10MB/2000ms=5MB/s），镜像快（10MB/500ms=20MB/s）
        stats.RecordSuccess("https://piston-meta.mojang.com/x.jar", 10 * 1024 * 1024, 2000);
        stats.RecordSuccess("https://bmclapi2.bangbang93.com/x.jar", 10 * 1024 * 1024, 500);

        var ranked = stats.Rank(["https://piston-meta.mojang.com/x.jar", "https://bmclapi2.bangbang93.com/x.jar"]);

        Assert.Equal("https://bmclapi2.bangbang93.com/x.jar", ranked[0]); // 快者优先
        Assert.Equal("https://piston-meta.mojang.com/x.jar", ranked[1]);
    }

    [Fact]
    public void Rank_NoData_KeepsDefaultOrder()
    {
        var stats = new SourceStats();

        var ranked = stats.Rank(["https://a.example.com/x.jar", "https://b.example.com/x.jar"]);

        Assert.Equal("https://a.example.com/x.jar", ranked[0]);
        Assert.Equal("https://b.example.com/x.jar", ranked[1]);
    }

    [Fact]
    public void Rank_ThreeFailures_DowngradedToEnd()
    {
        var stats = new SourceStats();
        // 官方连续失败 3 次 → 降权；镜像成功过 → 排前
        stats.RecordFailure("https://piston-meta.mojang.com/x.jar");
        stats.RecordFailure("https://piston-meta.mojang.com/x.jar");
        stats.RecordFailure("https://piston-meta.mojang.com/x.jar");
        stats.RecordSuccess("https://bmclapi2.bangbang93.com/x.jar", 1024, 100);

        var ranked = stats.Rank(["https://piston-meta.mojang.com/x.jar", "https://bmclapi2.bangbang93.com/x.jar"]);

        Assert.Equal("https://bmclapi2.bangbang93.com/x.jar", ranked[0]);
        Assert.Equal("https://piston-meta.mojang.com/x.jar", ranked[1]);
    }

    [Fact]
    public void Rank_SingleCandidate_Unchanged()
    {
        var stats = new SourceStats();
        stats.RecordFailure("https://a.example.com/x.jar");

        var ranked = stats.Rank(["https://a.example.com/x.jar"]);

        Assert.Single(ranked);
    }
}
