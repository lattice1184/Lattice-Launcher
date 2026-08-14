using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Launcher.App.Animations;

/// <summary>
/// ComboBox 下拉展开动画（Google Material 顶边缩放）：面板 scale 0.95→1 + 淡入（150ms fast-out-slow-in）。
/// 每次 DropDownOpened 现找模板内 Popup（不缓存——Popup 随模板重建）；找不到（自绘模板）静默跳过降级无动画。
/// 收起不做：DropDownClosed 在关闭后触发且不可取消，Material 收起本身很快。
/// 全局 ComboBox 样式 Setter 挂载。
/// </summary>
public static class DropDownAnimBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Enabled", typeof(DropDownAnimBehavior));

    static DropDownAnimBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<ComboBox>(OnEnabledChanged);
    }

    public static bool GetEnabled(Visual v) => v.GetValue(EnabledProperty);
    public static void SetEnabled(Visual v, bool value) => v.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(ComboBox c, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) c.DropDownOpened += OnDropDownOpened;
        else c.DropDownOpened -= OnDropDownOpened;
    }

    private static void OnDropDownOpened(object? s, System.EventArgs e)
    {
        if (s is not ComboBox combo) return;
        var panel = combo.FindDescendantOfType<Popup>()?.Child;
        if (panel is null) return; // 自绘模板无 Popup：降级无动画
        panel.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        panel.RenderTransform = new ScaleTransform(0.95, 0.95);
        panel.Opacity = 0;
        // host=panel 互斥：快速连点开关时打断旧动画重新起，无残留
        UiAnim.Animate(UiAnim.Durations.Fast, UiAnim.Curves.Standard, t =>
        {
            if (panel.RenderTransform is ScaleTransform st)
            {
                st.ScaleX = 0.95 + 0.05 * t;
                st.ScaleY = 0.95 + 0.05 * t;
            }
            panel.Opacity = t;
        }, null, panel);
    }
}
