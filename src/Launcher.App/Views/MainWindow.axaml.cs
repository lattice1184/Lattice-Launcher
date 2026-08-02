using Avalonia.Controls;
using Avalonia.Media;

namespace Launcher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // 窗口显示后 ActualTransparencyLevel 才为最终值；亚克力合成失败时切不透明，保证窗口永远可见
        Opened += (_, _) => ApplyOpacityFallback();
    }

    private void ApplyOpacityFallback()
    {
        if (ActualTransparencyLevel != WindowTransparencyLevel.None) return;
        if (RootSurface is null || NavSurface is null) return;
        RootSurface.Background = new SolidColorBrush(Color.Parse("#FF1A1C20"));
        NavSurface.IsVisible = false;
    }
}
