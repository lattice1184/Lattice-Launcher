using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>
/// 停滞透明化（AL68）：叶子/组失败时 Stage 亮明原因——用户卡在「正在完成…」死寂文案
/// 不知道在判死重试的根因。失败 → Stage「失败：原因」；重试前「源异常，自动重试中…」；
/// 组推导不再用「正在完成…」掩盖失败叶子。
/// </summary>
public class StageTransparencyTests
{
    private static DownloadManager CreateManager(DeferredSyncContext ctx) => new(ctx, 0);

    [Fact]
    public async Task LeafFailure_StageShowsReason()
    {
        // 叶子网络失败 → Stage「失败：连接被拒」而非死寂
        var ctx = new DeferredSyncContext();
        var mgr = CreateManager(ctx);
        var task = mgr.Enqueue("t", (p, c) => throw new HttpRequestException("连接被拒"));

        // REVIEW-B2：Completion 延迟到真正终态（重试耗尽）——重试链由 ctx 队列驱动，须先泵队列到终态
        // 注意等 Completion.IsCompleted 而非 State：State 首败即 Failed，Completion 要等 3 次尝试全部耗尽
        for (var i = 0; i < 1200 && !task.Completion.IsCompleted; i++)
        {
            ctx.Drain();
            await Task.Delay(10);
        }
        await task.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        ctx.Drain();
        // 等自动重试（AL69.1 共 3 次尝试：800ms + 3s 退避 + 重跑）全部失败——Stage 为最终失败原因
        // 等 Stage 而非 State：SetState 与 SetStage 是两次独立 Post，State 先生效 Stage 后生效
        for (var i = 0; i < 1200 && !(task.Stage?.Contains("失败") ?? false); i++)
        {
            await Task.Delay(10);
            ctx.Drain();
        }

        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Contains("失败", task.Stage ?? "");
        Assert.Contains("连接被拒", task.Stage ?? "");
    }

    [Fact]
    public async Task GroupWithFailedLeaf_StageShowsLeafReason_NotGenericComplete()
    {
        // 组内叶子失败 → 组 Stage 显示「失败：连接被拒」——不再退回「正在完成…」
        var ctx = new DeferredSyncContext();
        var mgr = CreateManager(ctx);
        var task = mgr.EnqueueGroup("组", async (gctx, ct) =>
        {
            gctx.AddChild("ok.jar", 100, (p, c) => Task.CompletedTask);
            gctx.AddChild("f.bin", 100, (p, c) => throw new HttpRequestException("连接被拒"));
        });

        await task.Completion;
        ctx.Drain();
        await Task.Delay(50);
        ctx.Drain();

        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Contains("连接被拒", task.Stage ?? "");
        Assert.DoesNotContain("正在完成", task.Stage ?? "");
    }

    [Fact]
    public async Task GroupSelfStage_ShownDuringNoLeafPhase()
    {
        // AL70：组任务无子任务阶段（asset index 前置）SetStage 要显示——不再退回「正在完成…」
        var ctx = new DeferredSyncContext();
        var mgr = CreateManager(ctx);
        var task = mgr.EnqueueGroup("组", async (gctx, ct) =>
        {
            gctx.SetStage("获取资源清单…");
            await Task.Delay(300, ct);
            gctx.AddChild("f.bin", 100, (p, c) => Task.CompletedTask);
        });

        ctx.Drain();
        for (var i = 0; i < 50 && task.Stage != "获取资源清单…"; i++)
        {
            await Task.Delay(10);
            ctx.Drain();
        }
        Assert.Equal("获取资源清单…", task.Stage); // 无叶子阶段显示编排 Stage

        await task.Completion;
        ctx.Drain();
        await Task.Delay(50);
        ctx.Drain();

        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal("已完成", task.Stage); // 终态清 _selfStage，不残留
    }

    [Fact]
    public async Task GroupWorkThrows_StageShowsReason()
    {
        // 组任务自身失败（编排层抛错）→ Stage 亮明原因。
        // 用 NotSupportedException（诊断 null → 不自动重试——网络类错误才会多轮重试）
        var ctx = new DeferredSyncContext();
        var mgr = CreateManager(ctx);
        var task = mgr.EnqueueGroup("组", (ctx, ct) => throw new NotSupportedException("清单损坏"));

        await task.Completion;
        ctx.Drain();
        await Task.Delay(50);
        ctx.Drain();

        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Contains("清单损坏", task.Stage ?? "");
    }

    [Fact]
    public async Task NetworkFailure_RetriesTwice_BeforeGivingUp()
    {
        // AL69.1 多轮机会：前 2 次网络失败 → 自动重试 2 次 → 第 3 次尝试成功（网络间歇恢复场景）
        var ctx = new DeferredSyncContext();
        var mgr = CreateManager(ctx);
        var calls = 0;
        var task = mgr.Enqueue("t", (p, c) =>
        {
            var n = Interlocked.Increment(ref calls);
            if (n < 3) throw new HttpRequestException("网络超时");
            return Task.CompletedTask;
        });

        // REVIEW-B2：Completion 延迟到真正终态——先泵队列驱动 2 次自动重试，终态后 Completion 才完成
        for (var i = 0; i < 1200 && task.State != DownloadTaskState.Completed; i++)
        {
            ctx.Drain();
            await Task.Delay(10);
        }
        await task.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        ctx.Drain();

        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(3, calls); // 初始 + 2 次自动重试（原 1 次会直接失败弹窗）
    }

    [Fact]
    public async Task Retry_StageResetsThenRuns()
    {
        // 自动重试：失败 → Stage「失败：…」→ 重试 Stage 清「重试中…」→ 最终成功
        var ctx = new DeferredSyncContext();
        var mgr = CreateManager(ctx);
        var calls = 0;
        var task = mgr.Enqueue("t", (p, c) =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new HttpRequestException("网络超时");
            return Task.CompletedTask;
        });

        // REVIEW-B2：Completion 延迟到自动重试后的真正终态——先泵队列驱动重试链
        for (var i = 0; i < 1200 && !task.Completion.IsCompleted; i++)
        {
            ctx.Drain();
            await Task.Delay(10);
        }
        await task.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        ctx.Drain();

        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.DoesNotContain("失败", task.Stage ?? "");
        Assert.Contains("已下载", task.Stage ?? ""); // 叶子完成 Stage
    }
}
