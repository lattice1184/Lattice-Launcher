using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Launcher.App.Animations;
using Launcher.App.ViewModels;
using Launcher.Core.Utils;

namespace Launcher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // 窗口显示后 ActualTransparencyLevel 才为最终值；亚克力合成失败时切不透明，保证窗口永远可见
        Opened += (_, _) =>
        {
            RestoreWindowSize();
            ApplyOpacityFallback();
            ApplyAppearance();
            // 页面切换：平滑滑入淡出
            PageHost.PageTransition = new UiAnim.FadeSlideTransition { Duration = TimeSpan.FromMilliseconds(180) };
            // Toast 出现时右侧滑入（NVIDIA 浮窗风；Opacity 淡入淡出由绑定驱动）
            ToastsHost.ContainerPrepared += (_, e) => UiAnim.SlideInX(e.Container);
            // 外观实时跟随设置页改动（保存应用 + 预览）
            if (DataContext is MainViewModel main)
            {
                main.Settings.AppearanceChanged += ApplyAppearance;
                main.Settings.PreviewChanged += ApplyAppearance;
            }
        };
        Closing += (_, _) => SaveWindowSize();
    }

    /// <summary>恢复上次窗口尺寸（设置记录；夹取到主屏工作区内并居中，防副屏关闭后不可见）</summary>
    private void RestoreWindowSize()
    {
        var s = LauncherSettings.Current;
        if (s.WindowWidth < MinWidth || s.WindowHeight < MinHeight) return;
        if (Screens.Primary?.WorkingArea is not { } wa) return;
        var w = Math.Min(s.WindowWidth, wa.Width);
        var h = Math.Min(s.WindowHeight, wa.Height);
        Width = w;
        Height = h;
        Position = new PixelPoint(
            wa.X + Math.Max((int)((wa.Width - w) / 2), 0),
            wa.Y + Math.Max((int)((wa.Height - h) / 2), 0));
    }

    /// <summary>关闭时记住窗口尺寸（下次启动恢复）</summary>
    private void SaveWindowSize()
    {
        var s = LauncherSettings.Current;
        s.WindowWidth = Width;
        s.WindowHeight = Height;
        s.Save();
    }

    private void ApplyOpacityFallback()
    {
        if (ActualTransparencyLevel != WindowTransparencyLevel.None) return;
        if (RootSurface is null || NavSurface is null) return;
        // 合成失败：亚克力材质回退纯色（Material.FallbackColor 已设；这里确保不透明）
        if (RootSurface.Material is ExperimentalAcrylicMaterial m)
            m.FallbackColor = Avalonia.Media.Color.Parse("#FF14181F");
        NavSurface.IsVisible = false;
    }

    /// <summary>应用外观设置：窗口透明度 + 界面密度（强调色由 App 应用）</summary>
    private void ApplyAppearance()
    {
        var s = LauncherSettings.Current;

        // 透明度：亚克力 TintOpacity 随设置（0.7-1.0 → 0.40-1.0 映射）
        if (RootSurface?.Material is ExperimentalAcrylicMaterial m)
        {
            m.TintOpacity = 0.40 + (s.WindowOpacity - 0.7) * 2.0; // 0.7→0.40，1.0→1.0
        }

        // 密度：整 UI 缩放（紧凑 0.9 / 标准 1.0 / 舒适 1.1）
        if (ContentSurface?.RenderTransform is Avalonia.Media.ScaleTransform scaleTransform)
        {
            var scale = s.Density switch
            {
                DensityMode.Compact => 0.9,
                DensityMode.Comfortable => 1.1,
                _ => 1.0,
            };
            scaleTransform.ScaleX = scale;
            scaleTransform.ScaleY = scale;
        }
    }
}
