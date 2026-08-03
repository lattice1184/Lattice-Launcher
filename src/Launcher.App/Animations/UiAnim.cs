using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Launcher.App.Animations;

/// <summary>
/// 全局动画工具：页面切换（淡入+弹性滑移）、弹性弹出（PopIn/SpringIn）。
/// 全部使用 Transform/Opacity（GPU 合成），DispatcherTimer 15ms 步进插值 + BackEase 弹性 overshoot，保证丝滑。
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

        /// <summary>从当前值平滑到目标透明度（位移 BackEase 弹性 overshoot——拉伸感；透明度 CubicEaseOut 防闪烁）</summary>
        private Task AnimateToAsync(Visual target, double toOpacity, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource();
            var steps = Math.Max(1, (int)(Duration.TotalMilliseconds / 15));
            var i = 0;
            var startOpacity = target.Opacity;
            var ease = BackOut;                            // 位移：弹性回弹（overshoot ~+37%）
            var fade = new CubicEaseOut();                 // 透明度：无 overshoot
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (_, _) =>
            {
                i++;
                var t = Math.Min(1.0, i / (double)steps);
                var e = ease(t);
                target.Opacity = Math.Clamp(startOpacity + (toOpacity - startOpacity) * fade.Ease(t), 0, 1);
                if (target.RenderTransform is TranslateTransform tr)
                {
                    tr.X *= (1 - e);
                    tr.Y *= (1 - e);
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

    /// <summary>弹性弹出（对话框）：scale 0.94→1 overshoot 回弹 + 淡入</summary>
    public static void PopIn(Visual root) => ElasticIn(root, 0.94);

    /// <summary>弹性放大进入（Popup 面板）：scale 0.90→1 overshoot 回弹 + 淡入</summary>
    public static void SpringIn(Visual root) => ElasticIn(root, 0.90);

    /// <summary>BackEase 弹性放大 + 淡入（拉伸变形感：越过目标再回弹）</summary>
    private static void ElasticIn(Visual root, double fromScale)
    {
        root.RenderTransform = new ScaleTransform(fromScale, fromScale);
        root.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        root.Opacity = 0;
        var steps = 18;
        var i = 0;
        var ease = BackOut;
        var fade = new CubicEaseOut();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            i++;
            var t = Math.Min(1.0, i / (double)steps);
            var e = ease(t);
            if (root.RenderTransform is ScaleTransform s)
            {
                var scale = fromScale + (1 - fromScale) * e; // overshoot → 越过 1.0 再回落
                s.ScaleX = scale;
                s.ScaleY = scale;
            }
            root.Opacity = Math.Clamp(fade.Ease(t), 0, 1);
            if (t >= 1.0) timer.Stop();
        };
        timer.Start();
    }

    /// <summary>BackEaseOut 弹性公式（t=0→0，t=1→1，中途 overshoot ~+37% 再回落——拉伸变形感；Avalonia 无内置 BackEase，本地实现）</summary>
    private static double BackOut(double t)
    {
        var p = 1 - t;
        return 1 - p * (p * p - Math.Sin(p * Math.PI));
    }
}
