using Avalonia.Controls;
using Avalonia.Media;
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
            ApplyOpacityFallback();
            ApplyAppearance();
            // 外观实时跟随设置页改动（保存应用 + 预览）
            if (DataContext is MainViewModel main)
            {
                main.Settings.AppearanceChanged += ApplyAppearance;
                main.Settings.PreviewChanged += ApplyAppearance;
            }
        };
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
