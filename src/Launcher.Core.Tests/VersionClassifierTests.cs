using Launcher.Core.Services;

namespace Launcher.Core.Tests;

/// <summary>版本分类：PCL2 愚人节表 / 日期启发 / combat 快照归快照 / 远古 / 正式</summary>
public class VersionClassifierTests
{
    private static VersionManifestService.GameVersionEntry Entry(string id, string type, DateTime? release = null)
        => new(id, type, false, release ?? new DateTime(2025, 6, 1), null, "");

    [Theory]
    [InlineData("26w14a")]
    [InlineData("24w14potato")]
    [InlineData("25w14craftmine")]
    [InlineData("1.rv-pre1")]
    [InlineData("3d shareware v1.34")]
    [InlineData("20w14infinite")]
    [InlineData("22w13oneblockatatime")]
    public void AprilFools_HardcodedList(string id)
    {
        Assert.True(VersionClassifier.IsAprilFools(Entry(id, "release")));
        Assert.Equal(VersionCategory.AprilFools, VersionClassifier.Classify(Entry(id, "release")));
    }

    [Fact]
    public void AprilFools_AprilFirstDateHeuristic()
    {
        var aprilFool = Entry("some-version", "release", new DateTime(2024, 4, 1));
        Assert.True(VersionClassifier.IsAprilFools(aprilFool));

        var normal = Entry("some-version", "release", new DateTime(2024, 4, 10));
        Assert.False(VersionClassifier.IsAprilFools(normal));
    }

    [Fact]
    public void CombatSnapshot_IsSnapshotNotAprilFools()
    {
        var combat = Entry("1.21.4-combat.1", "snapshot");
        Assert.False(VersionClassifier.IsAprilFools(combat)); // 行为修正：combat 不是愚人节
        Assert.Equal(VersionCategory.Snapshots, VersionClassifier.Classify(combat));
    }

    [Fact]
    public void Ancient_OldAlphaBeta()
    {
        Assert.Equal(VersionCategory.Ancient, VersionClassifier.Classify(Entry("c0.30_01c", "old_alpha")));
        Assert.Equal(VersionCategory.Ancient, VersionClassifier.Classify(Entry("b1.7.3", "old_beta")));
    }

    [Fact]
    public void Release_ClassifiesAsAllReleases()
    {
        var e = Entry("1.21.1", "release");
        Assert.Equal(VersionCategory.AllReleases, VersionClassifier.Classify(e));
        Assert.False(VersionClassifier.IsAprilFools(e));
    }
}
