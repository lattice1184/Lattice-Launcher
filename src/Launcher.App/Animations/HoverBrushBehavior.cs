using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System.Runtime.CompilerServices;

namespace Launcher.App.Animations;

/// <summary>
/// hover 变色（本地值驱动 + 150ms cubic 平滑过渡）：进入动画到 HoverBrush/HoverForeground，
/// 退出动画回基底色后 ClearValue 回落样式值（主题切换后样式仍生效）。
/// 根治 Avalonia 12 样式伪类 Setter 对模板 TemplateBinding 的驱动不可靠问题（nav 重做已验证本地值路线）；
/// 同槽位 "brush" 互斥——快速进出自动打断续接，无跳变无闪烁。
/// 挂载：全局 Button 样式 Enabled=True + 类样式指定 HoverBrush（Enter 时动态读取，样式 Setter 可配）。
/// </summary>
public static class HoverBrushBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Enabled", typeof(HoverBrushBehavior));

    public static readonly AttachedProperty<IBrush?> HoverBrushProperty =
        AvaloniaProperty.RegisterAttached<Visual, IBrush?>("HoverBrush", typeof(HoverBrushBehavior));

    public static readonly AttachedProperty<IBrush?> HoverForegroundProperty =
        AvaloniaProperty.RegisterAttached<Visual, IBrush?>("HoverForeground", typeof(HoverBrushBehavior));

    static HoverBrushBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static bool GetEnabled(Visual v) => v.GetValue(EnabledProperty);
    public static void SetEnabled(Visual v, bool value) => v.SetValue(EnabledProperty, value);
    public static IBrush? GetHoverBrush(Visual v) => v.GetValue(HoverBrushProperty);
    public static void SetHoverBrush(Visual v, IBrush? value) => v.SetValue(HoverBrushProperty, value);
    public static IBrush? GetHoverForeground(Visual v) => v.GetValue(HoverForegroundProperty);
    public static void SetHoverForeground(Visual v, IBrush? value) => v.SetValue(HoverForegroundProperty, value);

    private static void OnEnabledChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            c.PointerEntered += OnEntered;
            c.PointerExited += OnExited;
            c.PointerReleased += OnReleased;
            c.DetachedFromVisualTree += OnDetached;
        }
        else
        {
            c.PointerEntered -= OnEntered;
            c.PointerExited -= OnExited;
            c.PointerReleased -= OnReleased;
            c.DetachedFromVisualTree -= OnDetached;
        }
    }

    /// <summary>
    /// 8-16 离树兜底（「一直有」的另一半）：页面切换/窗口关闭时控件从视觉树移除，
    /// 进行中的 hover 动画被取消且不执行 done → 本地值残留；切回该页时按钮带着深色。
    /// 离树瞬间无条件清理本地值 + 复位状态（控件已离开，无视觉闪烁）。
    /// </summary>
    private static void OnDetached(object? s, VisualTreeAttachmentEventArgs e)
    {
        if (s is not Control c) return;
        var st = States.GetValue(c, _ => new HoverState());
        if (st.BgHovered || st.FgHovered)
        {
            c.ClearValue(TemplatedControl.BackgroundProperty);
            c.ClearValue(TemplatedControl.ForegroundProperty);
            st.BgHovered = false;
            st.FgHovered = false;
        }
    }

    /// <summary>每控件 hover 状态：退出动画的基底色（进入时捕获，此刻无本地值=样式值）+ 已动画通道标记</summary>
    private sealed class HoverState
    {
        public IBrush? BaseBg;
        public IBrush? BaseFg;
        public bool BgHovered;
        public bool FgHovered;
    }

    private static readonly ConditionalWeakTable<Control, HoverState> States = new();

    /// <summary>悬浮：捕获基底色（首次），动画到 hover 色（动态读 HoverBrush——样式 Setter 可配）</summary>
    private static void OnEntered(object? s, PointerEventArgs e)
    {
        if (s is not Control c) return;
        var st = States.GetValue(c, _ => new HoverState());
        if (!st.BgHovered && !st.FgHovered)
        {
            // 无本地值挂起——有效值即样式值，作为退出动画的基底（hover 中切主题的 1 帧回跳可接受）
            st.BaseBg = c.GetValue(TemplatedControl.BackgroundProperty);
            st.BaseFg = c.GetValue(TemplatedControl.ForegroundProperty);
        }
        if (c.GetValue(HoverBrushProperty) is { } brush)
        {
            st.BgHovered = true;
            UiAnim.TweenBrush(c, TemplatedControl.BackgroundProperty, brush, UiAnim.Durations.Fast);
        }
        if (c.GetValue(HoverForegroundProperty) is { } fg)
        {
            st.FgHovered = true;
            UiAnim.TweenBrush(c, TemplatedControl.ForegroundProperty, fg, UiAnim.Durations.Fast);
        }
    }

    /// <summary>移出：动画回基底色，完成后 ClearValue 回落样式值（样式求值当前态）</summary>
    private static void OnExited(object? s, PointerEventArgs e)
    {
        if (s is not Control c) return;
        var st = States.GetValue(c, _ => new HoverState());
        if (st.BgHovered)
        {
            UiAnim.TweenBrush(c, TemplatedControl.BackgroundProperty, st.BaseBg, UiAnim.Durations.Fast,
                done: () => { c.ClearValue(TemplatedControl.BackgroundProperty); st.BgHovered = false; });
        }
        if (st.FgHovered)
        {
            UiAnim.TweenBrush(c, TemplatedControl.ForegroundProperty, st.BaseFg, UiAnim.Durations.Fast,
                done: () => { c.ClearValue(TemplatedControl.ForegroundProperty); st.FgHovered = false; });
        }
    }

    /// <summary>
    /// 8-16 释放兜底（老残留 bug 根治）：按下后拖走 → 指针被捕获，PointerExited 不再触发，
    /// hover 本地值永久挂着（「悬浮过就变深恢复不回去」）。
    /// 注意不能用 IsPointerOver——它同样由 Entered/Exited 驱动，捕获中不更新（恒 true）；
    /// 用 Released 事件的真实指针坐标 + bounds 判断。
    /// </summary>
    private static void OnReleased(object? s, PointerReleasedEventArgs e)
    {
        if (s is not Control c) return;
        var pos = e.GetPosition(c);
        var inside = pos.X >= 0 && pos.Y >= 0 && pos.X <= c.Bounds.Width && pos.Y <= c.Bounds.Height;
        if (inside) return; // 指针仍在控件上（普通点击）→ hover 由 Exited 正常收尾
        var st = States.GetValue(c, _ => new HoverState());
        if (st.BgHovered)
        {
            c.ClearValue(TemplatedControl.BackgroundProperty);
            st.BgHovered = false;
        }
        if (st.FgHovered)
        {
            c.ClearValue(TemplatedControl.ForegroundProperty);
            st.FgHovered = false;
        }
    }
}
