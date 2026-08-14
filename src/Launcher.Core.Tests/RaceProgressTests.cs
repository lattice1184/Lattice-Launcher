using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>竞速进度合并（AL57）：多源同时拉同一文件副本时，进度单调累加且 cap 文件大小——不虚高</summary>
public class RaceProgressTests
{
    private const long MB = 1024 * 1024;
    private const long Total = 19 * MB;

    private static DownloadProgress P(long done) => new("", "f", done, Total, (int)(done * 100 / Total));

    [Fact]
    public void TwoSources_ShowsLeadingSourceOnly()
    {
        // 进度 = 领先源（所有源里已完成字节最大者）——多源拉同一文件副本，累加会造成
        // "下完了"错觉（限并发镜像下无源能赢，UI 却停 99%）
        var received = new List<DownloadProgress>();
        var h = DownloadService.RaceProgress.Wrap(2, received.Add);

        h.Handlers[0](P(10 * MB));   // 源1 领先 → 报 10MB
        h.Handlers[1](P(6 * MB));    // 源2 落后 → 不转发（max 不变）
        h.Handlers[0](P(Total));     // 源1 完成 → 报满

        Assert.Equal(2, received.Count);
        Assert.Equal(10 * MB, received[0].FileBytesDone);
        Assert.Equal(Total, received[1].FileBytesDone); // cap：不超文件大小
        for (var i = 1; i < received.Count; i++)
            Assert.True(received[i].FileBytesDone >= received[i - 1].FileBytesDone);
    }

    [Fact]
    public void RegressiveReport_IsIgnored()
    {
        // 同一源字节回退（源内部异常）→ 不覆盖领先值——UI 端永远看不到回退，计速基线不重置
        var received = new List<DownloadProgress>();
        var h = DownloadService.RaceProgress.Wrap(1, received.Add);

        h.Handlers[0](P(12 * MB));
        h.Handlers[0](P(8 * MB)); // 回退：8 < 12

        Assert.Single(received);
        Assert.Equal(12 * MB, received[0].FileBytesDone);
    }

    [Fact]
    public void FinalJump_ReportsFullOnlyAtEnd()
    {
        // 完成瞬间报满——DownloadTask 的累计/总时间平均速度显示真实值
        var received = new List<DownloadProgress>();
        var h = DownloadService.RaceProgress.Wrap(1, received.Add);

        h.Handlers[0](P(18 * MB));
        h.Handlers[0](P(Total));

        Assert.Equal(2, received.Count);
        Assert.Equal(Total, received[1].FileBytesDone);
    }
}
