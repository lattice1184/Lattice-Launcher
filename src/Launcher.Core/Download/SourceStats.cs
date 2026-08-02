using System.Collections.Concurrent;

namespace Launcher.Core.Download;

/// <summary>
/// 下载源质量统计（PCL2 SourceReport 模式）：按 host 记录速度与成败，
/// 候选源按平均速度排序（最快优先）、连续失败 ≥3 降权末尾、无数据保持默认顺序。
/// </summary>
public sealed class SourceStats
{
    private sealed record Stats(long TotalBytes, long TotalMs, int Success, int Fail);

    private const int DowngradeThreshold = 3;
    private readonly ConcurrentDictionary<string, Stats> _map = new(StringComparer.OrdinalIgnoreCase);

    public void RecordSuccess(string url, long bytes, long elapsedMs)
        => _map.AddOrUpdate(Host(url),
            _ => new Stats(bytes, elapsedMs, 1, 0),
            (_, s) => new Stats(s.TotalBytes + bytes, s.TotalMs + elapsedMs, s.Success + 1, s.Fail));

    public void RecordFailure(string url)
        => _map.AddOrUpdate(Host(url),
            _ => new Stats(0, 0, 0, 1),
            (_, s) => new Stats(s.TotalBytes, s.TotalMs, s.Success, s.Fail + 1));

    /// <summary>排序：平均速度降序；失败≥3 降权末尾；无数据保持默认顺序</summary>
    public IReadOnlyList<string> Rank(IReadOnlyList<string> candidates)
    {
        if (candidates.Count <= 1) return candidates;
        return candidates
            .Select((url, idx) => (Url: url, Idx: idx, Stats: _map.TryGetValue(Host(url), out var s) ? s : null))
            .OrderByDescending(x => Score(x.Stats))
            .ThenBy(x => x.Idx)
            .Select(x => x.Url)
            .ToList();
    }

    private static double Score(Stats? s)
    {
        if (s is null) return 0;                       // 无数据：默认顺序
        if (s.Fail >= DowngradeThreshold) return -1;   // 连续失败：降权末尾
        return (double)s.TotalBytes / Math.Max(s.TotalMs, 1);
    }

    private static string Host(string url) => new Uri(url).Host;
}
