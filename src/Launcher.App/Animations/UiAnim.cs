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
            var offset = 20.0;
            try
            {
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
                    to.Opacity = 1;
                    to.RenderTransform = null;
                }
            }
            finally
            {
                // 取消/中断也必须复位——防页面残留 Opacity=0 或位移（"页面没了"根因）
                if (to is not null)
                {
                    to.Opacity = 1;
                    to.RenderTransform = null;
                }
                if (from is not null && SlideOldOut) from.Opacity = 0;
            }
        }

        /// <summary>从当前值平滑到目标透明度（位移同步归零；CubicEaseInOut 平滑滑移——无 overshoot 防"不平滑"）</summary>
        private Task AnimateToAsync(Visual target, double toOpacity, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource();
            var steps = Math.Max(1, (int)(Duration.TotalMilliseconds / 15));
            var i = 0;
            var startOpacity = target.Opacity;
            var ease = new CubicEaseInOut();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (_, _) =>
            {
                i++;
                var t = Math.Min(1.0, i / (double)steps);
                var e = ease.Ease(t);
                target.Opacity = Math.Clamp(startOpacity + (toOpacity - startOpacity) * e, 0, 1);
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
            if (ct.CanBeCanceled)
                ct.Register(() => { timer.Stop(); tcs.TrySetResult(); });
            timer.Start();
            return tcs.Task;
        }
    }

    /// <summary>平滑弹出（对话框）：scale 0.96→1 + 淡入（CubicEaseOut，无弹跳——NVIDIA 浮窗风）</summary>
    public static void PopIn(Visual root) => ElasticIn(root, 0.96);

    /// <summary>平滑放大进入（Popup 面板）：scale 0.94→1 + 淡入（无弹跳）</summary>
    public static void SpringIn(Visual root) => ElasticIn(root, 0.94);

    /// <summary>CubicEaseOut 平滑放大 + 淡入（去弹性——用户实测弹跳不平滑）</summary>
    private static void ElasticIn(Visual root, double fromScale)
    {
        root.RenderTransform = new ScaleTransform(fromScale, fromScale);
        root.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        root.Opacity = 0;
        var steps = 16;
        var i = 0;
        var ease = new CubicEaseOut();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            i++;
            var t = Math.Min(1.0, i / (double)steps);
            var e = ease.Ease(t);
            if (root.RenderTransform is ScaleTransform s)
            {
                var scale = fromScale + (1 - fromScale) * e;
                s.ScaleX = scale;
                s.ScaleY = scale;
            }
            root.Opacity = Math.Clamp(e, 0, 1);
            if (t >= 1.0) timer.Stop();
        };
        timer.Start();
    }

    /// <summary>对话框通用平滑进出挂载：Opened 淡入 + Closing 拦截平滑切出后真正关闭（NVIDIA 浮窗风）</summary>
    public static void AttachDialog(Window win, Visual root)
    {
        win.Opened += (_, _) => PopIn(root);
        var closing = false;
        win.Closing += (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            SmoothOut(root, () => { closing = true; win.Close(); });
        };
    }

    /// <summary>平滑切出（关闭对话框）：scale 1→0.97 + 淡出（CubicEaseIn），完成回调后再真正关闭</summary>
    public static void SmoothOut(Visual root, Action? done = null)
    {
        root.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        if (root.RenderTransform is not ScaleTransform)
            root.RenderTransform = new ScaleTransform(1, 1);
        var startOpacity = root.Opacity;
        var steps = 14;
        var i = 0;
        var ease = new CubicEaseIn();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            i++;
            var t = Math.Min(1.0, i / (double)steps);
            var e = ease.Ease(t);
            root.Opacity = Math.Max(0, startOpacity * (1 - e));
            if (root.RenderTransform is ScaleTransform s)
            {
                s.ScaleX = 1 - 0.03 * e;
                s.ScaleY = 1 - 0.03 * e;
            }
            if (t >= 1.0)
            {
                timer.Stop();
                done?.Invoke();
            }
        };
        timer.Start();
    }
}
