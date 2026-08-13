using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.Runtime.CompilerServices;

namespace Launcher.App.Animations;

/// <summary>
/// 弹性缩放附加行为：hover 放大 1.02 / 按下压缩 0.96 / 释放回弹（BackEaseOut overshoot——拉伸变形丝滑感）。
/// 全局样式里以 <c>&lt;Setter Property="behaviors:SpringScale.Enabled" Value="True"/&gt;</c> 挂载到 Button / 列表行。
/// 帧驱动走全局 UiAnim（RAF，与显示器同步）；每控件一个持久 ScaleTransform 实例，每帧只改属性（零分配）。
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

    /// <summary>每控件持久 ScaleTransform（按钮上唯一变换源；值=1 时视觉恒等，可常驻）</summary>
    private static readonly ConditionalWeakTable<Visual, ScaleTransform> Transforms = new();

    /// <summary>从当前缩放弹性过渡到目标（BackOut overshoot 落在目标侧再回弹）。
    /// 同槽位 "scale" 互斥：新动画打断旧的，from 取当前中间值平滑续接（连点/快速进出无跳变）。</summary>
    private static void AnimateTo(Visual v, double to, int ms)
    {
        var st = Transforms.GetValue(v, _ => new ScaleTransform(1, 1));
        v.RenderTransform = st;
        var from = st.ScaleX; // 打断重入时从当前中间值起——平滑续接
        UiAnim.Animate(ms, UiAnim.Curves.Linear, e =>
        {
            var scale = from + (to - from) * BackOut(e);
            st.ScaleX = scale;
            st.ScaleY = scale; // 每帧零分配，只改属性
        }, null, v, slot: "scale");
    }

    /// <summary>BackEaseOut 弹性公式（0→1 进度，中途 overshoot ~+37% 越过目标再回落）</summary>
    private static double BackOut(double t)
    {
        var p = 1 - t;
        return 1 - p * (p * p - Math.Sin(p * Math.PI));
    }
}
