using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
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
        // Google 涟漪：扩散色 = 点击变深（BgBase #14181F——比所有按钮底色更暗；BgActive 反而比底色亮会显"白影"）
        var ellipse = new Ellipse
        {
            Width = 0,
            Height = 0,
            Fill = new SolidColorBrush(Color.Parse("#14181F")),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(ellipse, pos.X);
        Canvas.SetTop(ellipse, pos.Y);
        host.Children.Add(ellipse);

        // 扩散 + 淡出（~390ms 能看清；结束移除避免累积椭圆）
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
            ellipse.Opacity = 0.9 * (1 - e2); // 起始近乎不透明，扩散中淡出
            if (t >= 1.0)
            {
                timer.Stop();
                host.Children.Remove(ellipse);
            }
        };
        timer.Start();
    }
}
