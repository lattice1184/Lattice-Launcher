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
        }
        else
        {
            c.PointerEntered -= OnEntered;
            c.PointerExited -= OnExited;
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
}
