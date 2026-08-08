using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;

namespace Launcher.App.Animations;

/// <summary>
/// Expander 内容高度过渡：展开 0→h / 收起 h→0（220ms fast-out-slow-in，Material 扩展动画）。
/// 动画期间 ClipToBounds 防溢出；结束后恢复 Height=NaN（自动）+ 取消裁剪。
/// 重入安全：per-visual Inflight 表记录当前动画 tcs——新动画打断旧动画（内核互斥不触发 onDone），
/// 主动 TrySetResult 旧 tcs 唤醒其 finally，但 ReferenceEquals 校验保证只有最新动画才做复位。
/// 全局 Expander 样式 ContentTransition 挂载（Application.Resources 共享实例，无实例状态）。
/// </summary>
public sealed class ExpandCollapseTransition : IPageTransition
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(220);

    private static readonly ConcurrentDictionary<Visual, TaskCompletionSource> Inflight = new();

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken ct)
    {
        // 探针：真机跑一次确认 12.1.1 ContentTransition 的调用约定（展开/收起各自传什么）
        Debug.WriteLine($"[ExpandCollapse] Start from={(from?.GetType().Name ?? "null")} to={(to?.GetType().Name ?? "null")} forward={forward}");
        if (to is not null) { await Run(to, expanding: true, ct); return; }
        if (from is not null) { await Run(from, expanding: false, ct); return; }
    }

    private async Task Run(Visual target, bool expanding, CancellationToken ct)
    {
        if (target is not Control c) return;
        // 打断上一次动画：旧 tcs 提前完成 → 其 finally 因 ReferenceEquals 校验不复位，交棒给新动画
        if (Inflight.TryRemove(target, out var old)) old.TrySetResult();
        var tcs = new TaskCompletionSource();
        Inflight[target] = tcs;

        var startH = expanding ? 0.0 : c.DesiredSize.Height; // 展开从 0 起；收起从当前自然高度起
        var endH = expanding ? MeasureFullHeight(c) : 0.0;
        c.Height = startH;
        c.ClipToBounds = true;
        try
        {
            await AnimateToAsync(c, endH, ct, tcs);
        }
        finally
        {
            // 仅当仍是最新动画才复位（旧动画被打断后交棒，不得动新动画的布局）
            if (ReferenceEquals(Inflight.GetValueOrDefault(target), tcs))
            {
                Inflight.TryRemove(target, out _);
                c.Height = double.NaN;
                c.ClipToBounds = false;
            }
        }
    }

    /// <summary>内容未显示时先 Measure 一次拿完整高度（布局后 DesiredSize 即全高）</summary>
    private static double MeasureFullHeight(Control c)
    {
        c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return c.DesiredSize.Height;
    }

    private Task AnimateToAsync(Control c, double toH, CancellationToken ct, TaskCompletionSource tcs)
    {
        var startH = c.Height;
        // host=c 互斥（连点打断旧动画）；ct 取消走 onCancel → tcs 收尾 → finally 复位
        UiAnim.Animate(Duration.TotalMilliseconds, UiAnim.Curves.Standard, e =>
        {
            c.Height = startH + (toH - startH) * e;
        }, () => tcs.TrySetResult(), c, ct, () => tcs.TrySetResult());
        return tcs.Task;
    }
}
