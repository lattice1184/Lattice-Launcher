using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Launcher.App.Animations;

/// <summary>
/// hover 变色（本地值驱动）：PointerEntered 设 HoverBrush/HoverForeground（本地值优先级凌驾样式 Setter），
/// PointerExited 用 ClearValue 回落样式值（无需记录原色）。
/// 根治 Avalonia 12 样式伪类 Setter 对模板 TemplateBinding 的驱动不可靠问题（nav 重做已验证本地值路线）。
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

    /// <summary>悬浮：设本地值（动态读 HoverBrush——样式 Setter 可配）；无 HoverBrush 则只处理前景</summary>
    private static void OnEntered(object? s, PointerEventArgs e)
    {
        if (s is not Control c) return;
        if (c.GetValue(HoverBrushProperty) is { } brush)
            c.SetValue(TemplatedControl.BackgroundProperty, brush);
        if (c.GetValue(HoverForegroundProperty) is { } fg)
            c.SetValue(TemplatedControl.ForegroundProperty, fg);
    }

    /// <summary>移出：清本地值回落样式值（样式求值当前态）</summary>
    private static void OnExited(object? s, PointerEventArgs e)
    {
        if (s is not Control c) return;
        c.ClearValue(TemplatedControl.BackgroundProperty);
        c.ClearValue(TemplatedControl.ForegroundProperty);
    }
}
