using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Launcher.App.Animations;

/// <summary>
/// Material 涟漪（点击波纹）：PointerPressed 时从按压点扩散半透明圆并淡出。
/// 由 Button.nav 自定义模板内的 RippleHost(Canvas) 承载；全局样式 Setter 挂载。
/// </summary>
public static class RippleBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Enabled", typeof(RippleBehavior));

    static RippleBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static bool GetEnabled(Visual v) => v.GetValue(EnabledProperty);
    public static void SetEnabled(Visual v, bool value) => v.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) c.PointerPressed += OnPressed;
        else c.PointerPressed -= OnPressed;
    }

    private static void OnPressed(object? s, PointerPressedEventArgs e)
    {
        if (s is not Button btn) return;
        // 模板内唯一 Canvas 即 RippleHost（GetTemplateChild 受保护，走视觉树）
        if (btn.FindDescendantOfType<Canvas>() is not { } host) return;
        if (host.Bounds.Width <= 0 || host.Bounds.Height <= 0) return;

        var pos = e.GetPosition(host);
        var maxR = Math.Max(host.Bounds.Width, host.Bounds.Height) * 1.2;
        var ellipse = new Ellipse
        {
            Width = 0,
            Height = 0,
            Fill = new SolidColorBrush(Color.Parse("#33FFFFFF")),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(ellipse, pos.X);
        Canvas.SetTop(ellipse, pos.Y);
        host.Children.Add(ellipse);

        // 扩散 + 淡出（~390ms 放慢到能看清；结束移除避免累积椭圆）
        var steps = 26;
        var i = 0;
        var ease = new CubicEaseOut();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            i++;
            var t = Math.Min(1.0, i / (double)steps);
            var e2 = ease.Ease(t);
            var r = maxR * e2;
            ellipse.Width = r * 2;
            ellipse.Height = r * 2;
            Canvas.SetLeft(ellipse, pos.X - r);
            Canvas.SetTop(ellipse, pos.Y - r);
            ellipse.Opacity = 1 - e2;
            if (t >= 1.0)
            {
                timer.Stop();
                host.Children.Remove(ellipse);
            }
        };
        timer.Start();
    }
}
