using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;

namespace Launcher.App.Animations;

/// <summary>
/// Google Material 涟漪：PointerPressed 时从按压点扩散"点击后的颜色"（BgActive 压暗色）覆盖按钮。
/// RippleHost 通过 TemplateApplied 时模板内 FindName 缓存（防 FindDescendantOfType 找错 Content 内 Canvas）。
/// 全局 Button 样式 Setter 挂载（模板内置 RippleHost Canvas）。
/// </summary>
public static class RippleBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Enabled", typeof(RippleBehavior));

    private static readonly ConcurrentDictionary<Control, Canvas?> Hosts = new();

    static RippleBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static bool GetEnabled(Visual v) => v.GetValue(EnabledProperty);
    public static void SetEnabled(Visual v, bool value) => v.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            c.PointerPressed += OnPressed;
            if (c is TemplatedControl tc) tc.TemplateApplied += OnTemplateApplied;
        }
        else
        {
            c.PointerPressed -= OnPressed;
            if (c is TemplatedControl tc) tc.TemplateApplied -= OnTemplateApplied;
        }
    }

    /// <summary>模板应用后缓存 RippleHost（模板内唯一 Canvas；兜底才全树找）</summary>
    private static void OnTemplateApplied(object? s, TemplateAppliedEventArgs e)
    {
        if (s is not TemplatedControl c) return;
        Hosts[c] = FindHost(c);
    }

    // Avalonia 12 无 ControlTemplate 类（FindName 不可用）——视觉树找第一个 Canvas
    // （模板内唯一 Canvas 即 RippleHost；现有按钮 Content 均无 Canvas，安全）
    private static Canvas? FindHost(TemplatedControl c) => c.FindDescendantOfType<Canvas>();

    private static void OnPressed(object? s, PointerPressedEventArgs e)
    {
        if (s is not TemplatedControl c) return;
        if (!Hosts.TryGetValue(c, out var host) || host is null)
        {
            host = FindHost(c);
            if (host is null) return;
            Hosts[c] = host;
        }
        if (host.Bounds.Width <= 0 || host.Bounds.Height <= 0) return;

        var pos = e.GetPosition(host);
        var maxR = Math.Max(host.Bounds.Width, host.Bounds.Height) * 1.2;
        // Google 涟漪：8-18 批次 73 改半透明白（#40FFFFFF）——深色涟漪 #14181F 在导航深色玻璃
        // （#12161F）上融进背景不可见（用户实测"没有波纹"）；白色 25% alpha 在深色 UI 任何底上都可见
        // 一次布局定位（Canvas 坐标固定，圆心恒在按压点）+ 每帧只动 ScaleTransform/Opacity（零布局写、零分配）
        var ellipse = new Ellipse
        {
            Width = maxR * 2,                 // 终尺寸一次定死（布局只发生一次）
            Height = maxR * 2,
            Fill = new SolidColorBrush(Color.Parse("#40FFFFFF")),
            IsHitTestVisible = false,
            RenderTransform = new ScaleTransform(0, 0), // 预置 0，防首帧闪全尺寸；圆心=默认 50%,50%=按压点
            Opacity = 0.9,
        };
        Canvas.SetLeft(ellipse, pos.X - maxR);
        Canvas.SetTop(ellipse, pos.Y - maxR);
        host.Children.Add(ellipse);

        // 扩散 + 淡出（390ms 能看清；结束移除避免累积椭圆）。e2 数学与原版等价：半径 = e2·maxR
        var ease = new CubicEaseOut();
        UiAnim.Animate(390, UiAnim.Curves.Linear, e =>
        {
            var e2 = ease.Ease(e);
            var st = (ScaleTransform)ellipse.RenderTransform!;
            st.ScaleX = st.ScaleY = e2;
            ellipse.Opacity = 0.9 * (1 - e2); // 起始近乎不透明，扩散中淡出
        }, () => host.Children.Remove(ellipse), ellipse);
    }
}
