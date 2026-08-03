using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Launcher.App.Animations;

/// <summary>
/// 弹性缩放附加行为：hover 放大 1.02 / 按下压缩 0.96 / 释放回弹（BackEaseOut overshoot——拉伸变形丝滑感）。
/// 全局样式里以 <c>&lt;Setter Property="behaviors:SpringScale.Enabled" Value="True"/&gt;</c> 挂载到 Button / 列表行。
/// 手动 DispatcherTimer 插值（Avalonia 无 ScaleTransition，TransformOperations 字符串 XAML 不支持）。
/// </summary>
public static class SpringScaleBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Enabled", typeof(SpringScaleBehavior));

    static SpringScaleBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static bool GetEnabled(Visual v) => v.GetValue(EnabledProperty);
    public static void SetEnabled(Visual v, bool value) => v.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            c.PointerEntered += OnEnter;
            c.PointerExited += OnExit;
            c.PointerPressed += OnPressed;
            c.PointerReleased += OnReleased;
            c.PointerCaptureLost += OnCaptureLost;
        }
        else
        {
            c.PointerEntered -= OnEnter;
            c.PointerExited -= OnExit;
            c.PointerPressed -= OnPressed;
            c.PointerReleased -= OnReleased;
            c.PointerCaptureLost -= OnCaptureLost;
        }
    }

    private static void OnEnter(object? s, PointerEventArgs e) => AnimateTo((Visual)s!, 1.02, 220);
    private static void OnExit(object? s, PointerEventArgs e) => AnimateTo((Visual)s!, 1.0, 220);
    private static void OnPressed(object? s, PointerPressedEventArgs e) => AnimateTo((Visual)s!, 0.96, 150);
    private static void OnReleased(object? s, PointerEventArgs e) =>
        AnimateTo((Visual)s!, s is Control c && c.IsPointerOver ? 1.02 : 1.0, 260);
    private static void OnCaptureLost(object? s, PointerCaptureLostEventArgs e) => AnimateTo((Visual)s!, 1.0, 260);

    /// <summary>单个视觉元素正在跑的动画（新动画先打断旧的）</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Visual, DispatcherTimer> Timers = new();

    /// <summary>从当前缩放弹性过渡到目标（BackOut overshoot 落在目标侧再回弹）</summary>
    private static void AnimateTo(Visual v, double to, int ms)
    {
        if (Timers.TryGetValue(v, out var old)) old.Stop();

        var from = v.RenderTransform is ScaleTransform s ? s.ScaleX : 1.0;
        var steps = Math.Max(1, ms / 15);
        var i = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            i++;
            var t = Math.Min(1.0, i / (double)steps);
            var scale = from + (to - from) * BackOut(t);
            v.RenderTransform = new ScaleTransform(scale, scale);
            if (t >= 1.0)
            {
                timer.Stop();
                Timers.TryRemove(v, out _);
            }
        };
        Timers[v] = timer;
        timer.Start();
    }

    /// <summary>BackEaseOut 弹性公式（0→1 进度，中途 overshoot ~+37% 越过目标再回落）</summary>
    private static double BackOut(double t)
    {
        var p = 1 - t;
        return 1 - p * (p * p - Math.Sin(p * Math.PI));
    }
}
