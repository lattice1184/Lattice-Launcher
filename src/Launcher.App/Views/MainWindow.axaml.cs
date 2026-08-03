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
        RootSurface.Background = new SolidColorBrush(Color.Parse("#FF1A1C20"));
        NavSurface.IsVisible = false;
    }

    /// <summary>应用外观设置：窗口透明度 + 界面密度（强调色由 App 应用）</summary>
    private void ApplyAppearance()
    {
        var s = LauncherSettings.Current;

        // 透明度：RootSurface 背景 alpha = opacity（保持 #14181F 底色）
        if (RootSurface is not null)
        {
            var alpha = (byte)(s.WindowOpacity * 255);
            RootSurface.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x14, 0x18, 0x1F));
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
