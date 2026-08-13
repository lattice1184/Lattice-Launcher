using System.Diagnostics;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>
/// 下载体验根治（REVIEW-速度/进度/卡完成）：滑动窗口瞬时速度、聚合 percent 单调、组首败早退。
/// </summary>
public class DownloadSpeedTests
{
    private static DownloadProgress P(long done, long total) => new("", "f", done, total, total > 0 ? done * 100 / total : 0);

    [Fact]
    public async Task Speed_IsWindowed_NotCumulativeAverage()
    {
        // 前快后慢：开头 100MB/s、之后 ~0.2MB/s——旧「累计平均」显示 ~4MB/s，滑动窗口应 ~0.25MB/s
        var mgr = new DownloadManager(null);
        var task = mgr.Enqueue("t", (p, c) =>
        {
            p(P(0, 100 * 1024 * 1024));
            Thread.Sleep(100);
            p(P(10 * 1024 * 1024, 100 * 1024 * 1024));     // 100ms 内 10MB ≈ 100MB/s
            Thread.Sleep(400);
            p(P(10_500_000, 100 * 1024 * 1024));           // 400ms 内 500KB ≈ 1.25MB/s
            Thread.Sleep(2200);
            p(P(11_000_000, 100 * 1024 * 1024));           // 窗口末 500KB/2.2s ≈ 0.23MB/s
            return Task.CompletedTask;
        });
        await task.Completion;
        // 窗口近 2s ≈ 0.23MB/s；累计平均 ≈ 11MB/2.7s ≈ 4MB/s——断言显著低于平均
        Assert.True(task.SpeedBps < 1 * 1024 * 1024,
            $"窗口速度应 <1MB/s，实为 {task.SpeedBps / 1024 / 1024:0.0} MB/s（旧累计平均会显示 ~4MB/s）");
    }

    [Fact]
    public async Task GroupAggregate_PercentDoesNotDip_WhenChildMounted()
    {
        // 新子任务挂载（0% 起步）不拉低聚合 percent——旧实现加权回落 → 进度条回跳
        var mgr = new DownloadManager(null);
        var task = mgr.EnqueueGroup("g", async (ctx, ct) =>
        {
            var c1 = ctx.AddChild("a", 100, (p, c) =>
            {
                p(P(50, 100)); // 50%
                return Task.CompletedTask;
            });
            await c1.Completion;
            // 挂载 900 weight 的慢子任务（先报 10% 再拖 500ms）
            ctx.AddChild("b", 900, async (p, c) =>
            {
                p(P(10, 100)); // 10%
                await Task.Delay(500, c);
            });
        });
        // b 挂载并报 10% 后、完成前：加权 = 19% < 挂载前值（c1 完成即封顶 99）——必须保持不降
        for (var i = 0; i < 100 && task.ProgressPercent < 99; i++) await Task.Delay(10);
        var before = task.ProgressPercent;
        await Task.Delay(400); // REVIEW-节流：等 250ms 窗口 + 60ms 尾算跑完（b 10% 聚合已发布）
        Assert.True(task.ProgressPercent >= before,
            $"聚合 percent 被新子任务拉低：{before} → {task.ProgressPercent}");
        await task.Completion;
        Assert.Equal(DownloadTaskState.Completed, task.State);
    }

    [Fact]
    public async Task Group_FailsOnFirstChildFailure_DoesNotWaitForSlowSibling()
    {
        // 快失败 + 慢成功（2s）——组必须首败早退（≤1s），不等到慢兄弟下完（旧 WhenAll 会等满 2s）
        var mgr = new DownloadManager(null);
        var sw = Stopwatch.StartNew();
        var task = mgr.EnqueueGroup("g", async (ctx, ct) =>
        {
            ctx.AddChild("slow", 100, async (p, c) => { await Task.Delay(2000, c); });
            ctx.AddChild("fast-fail", 100, (p, c) => throw new HttpRequestException("404"));
        });
        await task.Completion;
        sw.Stop();
        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Contains("404", task.Error ?? "");
        Assert.True(sw.ElapsedMilliseconds < 1500,
            $"首败早退应 <1.5s，实际 {sw.ElapsedMilliseconds}ms（旧逻辑等满 2s 才失败）");
    }

    // ---------- ProgressReporter（REVIEW-治本：统一进度抽象，消灭「静默段」） ----------

    [Fact]
    public void Reporter_ThrottlesFastReports_To250msWindow()
    {
        // 高速下载：250ms 内多次 Report 只允许 1 次 emit（不刷爆 UI Post 队列）
        var emitted = new List<DownloadProgress>();
        var rep = new ProgressReporter("阶段", emitted.Add);
        rep.Report(1, 100);        // 窗口内（构造时已 emit 一次）
        rep.Report(2, 100);
        rep.Report(3, 100);
        Assert.Single(emitted);    // 构造 emit，三次 Report 全被节流
        Thread.Sleep(300);
        rep.Report(50, 100);       // 窗口过后
        Assert.Equal(2, emitted.Count);
        Assert.Equal(50, emitted[^1].FileBytesDone);
    }

    [Fact]
    public void Reporter_Complete_EmitsLastStateInsideWindow()
    {
        // Complete 补报：节流窗口内最后状态不丢（收尾强制 emit）
        var emitted = new List<DownloadProgress>();
        var rep = new ProgressReporter("阶段", emitted.Add);
        rep.Report(80, 100);       // 被节流吞掉
        rep.Complete();            // 必须补报
        Assert.Equal(2, emitted.Count);
        Assert.Equal(80, emitted[^1].FileBytesDone);
        Assert.Equal(100, emitted[^1].FileTotalBytes);
    }

    [Fact]
    public void Reporter_ReportStage_EmitsImmediately()
    {
        // 阶段文字变化必须立即可见（文字优先于节流）——「静默段」根除点
        var emitted = new List<DownloadProgress>();
        var rep = new ProgressReporter("正在下载…", emitted.Add);
        rep.ReportStage("正在质检 3/10…");   // 窗口内也要立即生效
        Assert.Equal(2, emitted.Count);
        Assert.Equal("正在质检 3/10…", emitted[^1].Stage);
    }

    [Fact]
    public void Reporter_NullSink_IsNoOp()
    {
        // 无消费端（sink=null）：全方法空操作，调用方无需判空分支
        var rep = new ProgressReporter("阶段", null);
        rep.Report(1, 2);
        rep.ReportStage("新阶段");
        rep.Complete();
    }
}
