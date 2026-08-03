using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Launcher.App.Animations;

/// <summary>
/// 全局动画工具：页面切换（淡入+滑移）、弹出（PopIn）。
/// 全部使用 Transform/Opacity（GPU 合成），DispatcherTimer 15ms 步进插值，保证丝滑。
/// </summary>
public static class UiAnim
{
    /// <summary>页面切换：旧页淡出左移，新页右滑淡入（0.22s CubicEaseInOut）</summary>
    public sealed class FadeSlideTransition : IPageTransition
    {
        public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(220);
        public bool SlideOldOut { get; set; } = true;

        public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken ct)
        {
            var offset = 24.0;

            if (to is not null)
            {
                to.Opacity = 0;
                to.RenderTransform = new TranslateTransform(forward ? offset : -offset, 0);
            }
            if (from is not null && SlideOldOut)
            {
                await AnimateToAsync(from, 0.0, ct);
                from.Opacity = 0;
            }
            if (to is not null)
            {
                await AnimateToAsync(to, 1.0, ct);
                to.RenderTransform = null;
            }
        }

        /// <summary>从当前值平滑到目标透明度（RenderTransform 位移同步归零）</summary>
        private Task AnimateToAsync(Visual target, double toOpacity, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource();
            var steps = Math.Max(1, (int)(Duration.TotalMilliseconds / 15));
            var i = 0;
            var startOpacity = target.Opacity;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (_, _) =>
            {
                i++;
                var t = Math.Min(1.0, i / (double)steps);
                var ease = new CubicEaseInOut().Ease(t);
                target.Opacity = startOpacity + (toOpacity - startOpacity) * ease;
                if (target.RenderTransform is TranslateTransform tr)
                {
                    tr.X *= (1 - ease);
                    tr.Y *= (1 - ease);
                }
                if (t >= 1.0)
                {
                    timer.Stop();
                    tcs.TrySetResult();
                }
            };
            timer.Start();
            return tcs.Task;
        }
    }

    /// <summary>弹出动画（对话框/详情面板）：scale 0.96→1 + 淡入 0.18s</summary>
    public static void PopIn(Visual root)
    {
        root.RenderTransform = new ScaleTransform(0.96, 0.96);
        root.Opacity = 0;
        var steps = 12;
        var i = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            i++;
            var t = Math.Min(1.0, i / (double)steps);
            var ease = new CubicEaseOut().Ease(t);
            if (root.RenderTransform is ScaleTransform s)
            {
                s.ScaleX = 0.96 + 0.04 * ease;
                s.ScaleY = 0.96 + 0.04 * ease;
            }
            root.Opacity = ease;
            if (t >= 1.0) timer.Stop();
        };
        timer.Start();
    }
}
