namespace Launcher.Core.Services;

/// <summary>版本分类（PCL2 风格：最新正式/全部正式/快照/远古/愚人节）</summary>
public enum VersionCategory { LatestRelease, AllReleases, Snapshots, Ancient, AprilFools }

/// <summary>
/// 版本分类器：愚人节采用 PCL2 硬编码表 + 4/1 日期启发；
/// combat 快照归快照（不是愚人节）；latest 由 UI 取正式版前 N 个。
/// </summary>
public static class VersionClassifier
{
    public const int LatestReleaseCount = 5;

    private static readonly HashSet<string> AprilFoolsIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "2point0_paradigm", "2point0_snapshot", "20w14infinite", "3d shareware v1.34",
        "1.rv-pre1", "15w14a", "22w13oneblockatatime", "23w13a_or_b", "24w14potato",
        "25w14craftmine", "26w14a",
    };

    public static bool IsAprilFools(VersionManifestService.GameVersionEntry e)
    {
        if (e.ReleaseTime is { Month: 4, Day: <= 3 }) return true;
        return AprilFoolsIds.Contains(e.Id);
    }

    /// <summary>基础分类（LatestRelease 是 AllReleases 的前 N 个，由 UI 层取）</summary>
    public static VersionCategory Classify(VersionManifestService.GameVersionEntry e)
    {
        if (IsAprilFools(e)) return VersionCategory.AprilFools;
        if (e.Type == "release") return VersionCategory.AllReleases;
        if (e.Type == "snapshot") return VersionCategory.Snapshots;
        return VersionCategory.Ancient; // old_alpha / old_beta
    }
}
