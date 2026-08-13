using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Launcher.App.Animations;

/// <summary>
/// 全局动画工具：页面切换（淡入+滑移）、平滑弹出、对话框进出场、Toast 滑入。
/// 全部使用 Transform/Opacity（GPU 合成，不触发 layout）。
/// 驱动方式：渲染帧驱动（TopLevel.RequestAnimationFrame，跟着屏幕刷新走，GC/UI 忙不掉帧）；
/// 无可用 TopLevel（启动早期）回退 DispatcherTimer 15ms。单帧统一步进全部活动动画，
/// Stopwatch 绝对起算——动画被遮挡/最小化后恢复时超时帧直接收尾，终值必达。
/// </summary>
public static class UiAnim
{
    /// <summary>全局宿主（MainWindow Opened 时注册）——渲染帧驱动从这里取 TopLevel。</summary>
    public static Window? Host;

    // ===== 曲线令牌（Material 运动曲线）=====
    public static class Curves
    {
        /// <summary>fast-out-slow-in：标准交互曲线（hover/按下/进入淡入）</summary>
        public static readonly IEasing Standard = new SplineEasing(0.4, 0, 0.2, 1);
        /// <summary>linear-out-slow-in：进入类动画（页面/内容滑入），慢收尾</summary>
        public static readonly IEasing Decelerate = new SplineEasing(0, 0, 0.2, 1);
        /// <summary>fast-out-linear-in：退出类动画（淡出/滑出），快起匀速收</summary>
        public static readonly IEasing Accelerate = new SplineEasing(0.4, 0, 1, 1);
        /// <summary>强调曲线（350ms 弹簧过冲，按钮释放回弹/logo 出现）</summary>
        public static readonly IEasing Overshoot = CreateSpring(0.6, 120, 1, 0);
        /// <summary>线性进度：调用方自带缓动公式时用（BackOut/CubicEaseOut 已把 e 再做一层 Ease）</summary>
        public static readonly IEasing Linear = new LinearEasing();
    }

    /// <summary>标准时长令牌（ms）</summary>
    public static class Durations
    {
        public const double Fast = 150;     // 微交互：hover/按下/淡入
        public const double Standard = 220; // 常规：页面切换/滑入
        public const double Emphasis = 350; // 强调：回弹/大对象
    }

    /// <summary>阻尼弹簧 easing 工厂：阻尼比 ζ（0.6~0.85 为干净回弹），stiffness 决定回弹速度，
    /// mass 一般 1。换算 damping = 2ζ√(mass·stiffness)。终值精确=1 由内核在 t>=1 时钳位
    /// （IEasing 不可由用户代码实现，无法包装；内核统一钳位保证 finally 复位语义不漂移）。</summary>
    public static IEasing CreateSpring(double dampingRatio, double stiffness, double mass = 1, double initialVelocity = 0)
    {
        var damping = 2 * dampingRatio * Math.Sqrt(mass * stiffness);
        return new SpringEasing { Mass = mass, Stiffness = stiffness, Damping = damping, InitialVelocity = initialVelocity };
    }

    // ===== 帧驱动内核 =====
    private sealed class ActiveAnim
    {
        public Visual? Visual;
        public double StartMs;
        public double DurationMs;
        public IEasing Curve;
        public Action<double> Set;
        public Action? Done;
        public bool Canceled;
        public IDisposable? CancelReg;
        public string? Slot;
    }

    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly List<ActiveAnim> Active = new();
    private static DispatcherTimer? _fallbackTimer;

    /// <summary>统一动画内核：渲染帧驱动插值 0→1（曲线已缓动），同 visual+同槽位新动画打断旧动画（不写终值）；
    /// onCancel 在 ct 取消时调用（不写终值）；onDone 在正常到 1.0 时调用。
    /// slot：互斥粒度细分——同一 visual 上可并行多路动画（如 hover 颜色 "brush" 与缩放 "scale"），
    /// 同槽位才互斥打断。旧调用不传 slot → 行为与原先完全一致（null 槽位）。</summary>
    public static void Animate(double ms, IEasing curve, Action<double> set, Action? onDone = null,
        Visual? host = null, CancellationToken ct = default, Action? onCancel = null, string? slot = null)
    {
        var now = Clock.Elapsed.TotalMilliseconds;
        var anim = new ActiveAnim
        {
            Visual = host,
            StartMs = now,
            DurationMs = Math.Max(1, ms),
            Curve = curve,
            Set = set,
            Done = onDone,
            Slot = slot,
        };
        // 每 visual+槽位互斥：同 visual 同槽位已有动画直接打断（不写终值，不触发 done）。host=null 不互斥
        if (host is not null)
        {
            for (var i = Active.Count - 1; i >= 0; i--)
            {
                var a = Active[i];
                if (!a.Canceled && ReferenceEquals(a.Visual, host) && a.Slot == slot)
                {
                    a.Canceled = true;
                    a.CancelReg?.Dispose();
                }
            }
        }
        if (ct.CanBeCanceled)
        {
            anim.CancelReg = ct.Register(() =>
            {
                anim.Canceled = true;
                onCancel?.Invoke();
            });
        }
        Active.Add(anim);
        RequestFrame();
    }

    private static void RequestFrame()
    {
        var top = Host;
        if (top is not null && !top.IsActive) top = null;
        if (top is not null)
        {
            _fallbackTimer?.Stop();
            top.RequestAnimationFrame(OnFrame);
            return;
        }
        // 无 TopLevel：启动早期兜底，DispatcherTimer 驱动同一 Step
        _fallbackTimer ??= CreateFallbackTimer();
        if (!_fallbackTimer.IsEnabled) _fallbackTimer.Start();

        static DispatcherTimer CreateFallbackTimer()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            t.Tick += (_, _) => Step();
            return t;
        }
    }

    private static void OnFrame(TimeSpan _) => Step();

    private static void Step()
    {
        var now = Clock.Elapsed.TotalMilliseconds;
        FrameStats.Tick(now);
        for (var i = Active.Count - 1; i >= 0; i--)
        {
            var a = Active[i];
            if (a.Canceled)
            {
                a.CancelReg?.Dispose();
                Active.RemoveAt(i);
                continue;
            }
            var t = Math.Min(1.0, (now - a.StartMs) / a.DurationMs);
            // 终值钳位：t>=1 强制 e=1（弹簧曲线末尾可能有残余位移，收尾必须精确落定）
            a.Set(t >= 1.0 ? 1.0 : a.Curve.Ease(t));
            if (t >= 1.0)
            {
                a.CancelReg?.Dispose();
                Active.RemoveAt(i);
                a.Done?.Invoke();
            }
        }
        if (Active.Count > 0) RequestFrame();
        else _fallbackTimer?.Stop();
    }

    // ===== 公共动画（签名保持，内部全部走帧内核）=====

    /// <summary>页面切换：旧页淡出左移，新页右滑淡入（Material fast-out-slow-in）</summary>
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

        /// <summary>从当前值平滑到目标透明度（位移同步归零）</summary>
        private Task AnimateToAsync(Visual target, double toOpacity, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource();
            var startOpacity = target.Opacity;
            Animate(Duration.TotalMilliseconds, Curves.Standard, e =>
            {
                target.Opacity = Math.Clamp(startOpacity + (toOpacity - startOpacity) * e, 0, 1);
                if (target.RenderTransform is TranslateTransform tr)
                {
                    tr.X *= (1 - e);
                    tr.Y *= (1 - e);
                }
            }, () => tcs.TrySetResult(), target, ct, () => tcs.TrySetResult());
            return tcs.Task;
        }
    }

    /// <summary>平滑弹出（对话框）：scale 0.96→1 + 淡入（Material 进入曲线，无弹跳——NVIDIA 浮窗风）</summary>
    public static void PopIn(Visual root) => ElasticIn(root, 0.96);

    /// <summary>平滑放大进入（Popup 面板）：scale 0.94→1 + 淡入（无弹跳）</summary>
    public static void SpringIn(Visual root) => ElasticIn(root, 0.94);

    /// <summary>进入曲线平滑放大 + 淡入（去弹性——用户实测弹跳不平滑）</summary>
    private static void ElasticIn(Visual root, double fromScale)
    {
        root.RenderTransform = new ScaleTransform(fromScale, fromScale);
        root.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        root.Opacity = 0;
        Animate(Durations.Standard, Curves.Decelerate, e =>
        {
            if (root.RenderTransform is ScaleTransform s)
            {
                var scale = fromScale + (1 - fromScale) * e;
                s.ScaleX = scale;
                s.ScaleY = scale;
            }
            root.Opacity = Math.Clamp(e, 0, 1);
        }, null, root);
    }

    /// <summary>对话框通用右侧切入切出（NVIDIA 浮窗风）：整窗淡入 + 内容从右 48px 滑入；关闭内容滑出 + 整窗淡出后真正关闭</summary>
    public static void AttachDialog(Window win, Visual root)
    {
        win.Opened += (_, _) =>
        {
            win.Opacity = 0;
            FadeTo(win, 1.0, 200);
            SlideInFromRight(root);
        };
        var closing = false;
        win.Closing += (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            SlideOutToRight(root); // 明显横向位移（用户实测：只有淡出太突兀）
            FadeTo(win, 0.0, 200, () => { closing = true; win.Close(); });
        };
    }

    /// <summary>右侧切入：内容从右 48px 横向滑入 + 淡入（进入曲线）</summary>
    public static void SlideInFromRight(Visual root)
    {
        root.RenderTransform = new TranslateTransform(48, 0);
        root.Opacity = 0;
        Animate(Durations.Standard, Curves.Decelerate, e =>
        {
            if (root.RenderTransform is TranslateTransform tr) tr.X = 48 * (1 - e);
            root.Opacity = Math.Clamp(e, 0, 1);
        }, () => root.RenderTransform = null, root);
    }

    /// <summary>Toast 右侧滑入：只动画位移 48→0（不动 Opacity——Toast 淡入淡出由 ToastItem.Opacity 绑定驱动，动会覆盖绑定破坏淡出）</summary>
    public static void SlideInX(Visual root, double fromX = 48, int ms = 220)
    {
        root.RenderTransform = new TranslateTransform(fromX, 0);
        Animate(ms, Curves.Decelerate, e =>
        {
            if (root.RenderTransform is TranslateTransform tr) tr.X = fromX * (1 - e);
        }, () => root.RenderTransform = null, root);
    }

    /// <summary>右侧切出：内容滑出到右 48px + 淡出（退出曲线；窗口淡出负责真正关闭）</summary>
    public static void SlideOutToRight(Visual root, Action? done = null)
    {
        if (root.RenderTransform is not TranslateTransform)
            root.RenderTransform = new TranslateTransform(0, 0);
        var startOpacity = root.Opacity;
        Animate(Durations.Fast, Curves.Accelerate, e =>
        {
            if (root.RenderTransform is TranslateTransform tr) tr.X = 48 * e;
            root.Opacity = Math.Max(0, startOpacity * (1 - e));
        }, done, root);
    }

    /// <summary>窗口透明度插值（淡入淡出；关闭用退出曲线，出现用进入曲线）</summary>
    private static void FadeTo(Window win, double to, int ms, Action? done = null)
    {
        var start = win.Opacity;
        var curve = to > start ? Curves.Decelerate : Curves.Accelerate;
        Animate(ms, curve, e => win.Opacity = start + (to - start) * e, done, win);
    }

    /// <summary>平滑切出（关闭对话框）：scale 1→0.97 + 淡出（退出曲线），完成回调后再真正关闭</summary>
    public static void SmoothOut(Visual root, Action? done = null)
    {
        root.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        if (root.RenderTransform is not ScaleTransform)
            root.RenderTransform = new ScaleTransform(1, 1);
        var startOpacity = root.Opacity;
        Animate(Durations.Fast, Curves.Accelerate, e =>
        {
            root.Opacity = Math.Max(0, startOpacity * (1 - e));
            if (root.RenderTransform is ScaleTransform s)
            {
                s.ScaleX = 1 - 0.03 * e;
                s.ScaleY = 1 - 0.03 * e;
            }
        }, done, root);
    }

    /// <summary>控件画刷属性颜色过渡（hover/按下平滑渐变）：非 SolidColorBrush 源/目标 → SetValue 瞬跳兜底；
    /// 否则复用单个 SolidColorBrush 实例每帧只改 Color（零分配，渲染属性非布局），完成时写回目标 brush。
    /// 同槽位互斥：快速进出自动打断旧动画、从当前中间色续接（无跳变、无闪烁）。</summary>
    public static void TweenBrush(Control c, StyledProperty<IBrush?> prop, IBrush? to,
        double ms = Durations.Fast, string? slot = "brush", Action? done = null)
    {
        var from = c.GetValue(prop);
        if (from is not SolidColorBrush f || to is not SolidColorBrush t)
        {
            c.SetValue(prop, to);
            done?.Invoke();
            return;
        }
        var brush = new SolidColorBrush(f.Color); // 每个动画一次分配；帧内只改 Color
        c.SetValue(prop, brush);
        Animate(ms, Curves.Standard, e => { brush.Color = LerpColor(f.Color, t.Color, e); }, () =>
        {
            c.SetValue(prop, to); // 写回样式定义的 brush 实例（本地值不留匿名 brush）
            done?.Invoke();
        }, c, slot: slot);
    }

    /// <summary>逐通道颜色插值（不依赖 Color.Lerp 版本差异；与 MainWindow.LerpBrush 同算法）</summary>
    private static Color LerpColor(Color from, Color to, double e) => Color.FromArgb(
        (byte)(from.A + (to.A - from.A) * e),
        (byte)(from.R + (to.R - from.R) * e),
        (byte)(from.G + (to.G - from.G) * e),
        (byte)(from.B + (to.B - from.B) * e));

    /// <summary>帧率统计（环境变量 LATTICE_FRAMESTATS=1 开启，默认关——Enabled=false 时每帧仅一次 bool 判断）：
    /// Step 内测相邻帧间隔，2s 滑动窗口输出 avg/max/p99 + GC0 + 分配量。
    /// 全方案观测点——迁移后所有动画都汇入 UiAnim，这里直接反映动画期间 UI 线程帧健康度。</summary>
    public static class FrameStats
    {
        /// <summary>环境变量 LATTICE_FRAMESTATS=1 自动开启（DEBUG 构建验收开关；正式版零开销）</summary>
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("LATTICE_FRAMESTATS") == "1";
        private static double _lastMs = -1;
        private static double _windowStart;
        private static readonly List<double> Sizes = new();
        private static long _allocBase;
        private static int _gcBase;

        public static void Tick(double nowMs)
        {
            if (!Enabled) return;
            if (_lastMs < 0)
            {
                _lastMs = nowMs;
                _windowStart = nowMs;
                _allocBase = GC.GetTotalAllocatedBytes(true);
                _gcBase = GC.CollectionCount(0);
                return;
            }
            Sizes.Add(nowMs - _lastMs);
            _lastMs = nowMs;
            if (nowMs - _windowStart < 2000) return;
            Sizes.Sort();
            var avg = Sizes.Average();
            var max = Sizes[^1];
            var p99 = Sizes[(int)(Sizes.Count * 0.99) - 1];
            System.Diagnostics.Debug.WriteLine(
                $"[FrameStats] {Sizes.Count}帧 avg={avg:0.0}ms max={max:0.0}ms p99={p99:0.0}ms " +
                $"| GC0 +{GC.CollectionCount(0) - _gcBase} | alloc +{(GC.GetTotalAllocatedBytes(true) - _allocBase) / 1024.0:0.0}KB");
            var line = $"[FrameStats] {Sizes.Count}帧 avg={avg:0.0}ms max={max:0.0}ms p99={p99:0.0}ms " +
                $"| GC0 +{GC.CollectionCount(0) - _gcBase} | alloc +{(GC.GetTotalAllocatedBytes(true) - _allocBase) / 1024.0:0.0}KB";
            System.Diagnostics.Debug.WriteLine(line);
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "framestats.log"), line + Environment.NewLine); } catch { }
            Sizes.Clear();
            _windowStart = nowMs;
            _lastMs = -1; // 窗口重开，重新校准
            _allocBase = GC.GetTotalAllocatedBytes(true);
            _gcBase = GC.CollectionCount(0);
        }
    }
}
