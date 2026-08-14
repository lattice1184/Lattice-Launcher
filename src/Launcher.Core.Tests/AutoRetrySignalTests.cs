using System.Net.Http;
using System.Threading;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>
/// 8-18 自动重试信号（UI 弹「正在自动重试」提示的数据源）：
/// IsAutoRetryPending 首败 true/耗尽 false；AutoRetryScheduled 事件携带 (attempt, total)。
/// </summary>
public class AutoRetrySignalTests
{
    private static async Task DrainUntil(DeferredSyncContext ctx, Func<bool> done)
    {
        // 不设次数上限：完整重试链（800ms + 3000ms + 泵开销）在全量并行下可能被线程池饥饿拉长到
        // 数十秒（08-12 实测：Task.Run 续跑排队饿死 20s+）——上限会误报超时，产品环境是真实 UI 队列无此问题
        while (!done())
        {
            await Task.Delay(10);
            ctx.Drain();
        }
    }

    [Fact]
    public async Task FirstFailure_PendingTrue_EventFires()
    {
        var ctx = new DeferredSyncContext();
        var mgr = new DownloadManager(ctx, 0);
        var events = new List<DownloadTask.AutoRetryArgs>();
        var task = mgr.Enqueue("首败", (_, _) => throw new HttpRequestException("网络超时"));
        task.AutoRetryScheduled += (_, e) => events.Add(e);

        // 首败 → 排程自动重试（State 短暂 Failed 后重试中）
        await DrainUntil(ctx, () => task.IsAutoRetryPending);
        ctx.Drain();

        Assert.True(task.IsAutoRetryPending);          // 重试排程中（UI 抑制「失败」Toast 用）
        Assert.Equal([(1, 2)], events.Select(e => (e.Attempt, e.Total))); // 事件已带次数
    }

    [Fact]
    public async Task Exhausted_PendingFalse_EventFiredTwice()
    {
        var ctx = new DeferredSyncContext();
        var mgr = new DownloadManager(ctx, 0);
        var events = new List<DownloadTask.AutoRetryArgs>();
        var task = mgr.Enqueue("恒败", (_, _) => throw new HttpRequestException("网络超时"));
        task.AutoRetryScheduled += (_, e) => events.Add(e);

        // 泵完整条自动重试链：首败(1,2) → 800ms → 重试败(2,2) → 3s → 重试败 → 耗尽终态
        await DrainUntil(ctx, () => task.Completion.IsCompleted);
        ctx.Drain();

        Assert.False(task.IsAutoRetryPending);         // 耗尽：终态失败（UI 弹「失败」Toast）
        Assert.Equal([(1, 2), (2, 2)], events.Select(e => (e.Attempt, e.Total)));
    }
}
