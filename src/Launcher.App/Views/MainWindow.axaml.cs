using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Launcher.App.Animations;
using Launcher.App.ViewModels;
using Launcher.Core.Utils;

namespace Launcher.App.Views;

public partial class MainWindow : Window
{
    /// <summary>导航按钮注册表（页面 id → 按钮）。视觉全部用本地值驱动——本地值优先级凌驾样式 Setter，
    /// 模板 TemplateBinding 实时跟随，根治样式伪类在 Avalonia 12 下 hover 失效 / pressed 白影的问题。</summary>
    private readonly Dictionary<string, Button> _navButtons = new();

    public MainWindow()
    {
        InitializeComponent();
        _navButtons["home"] = NavHome;
        _navButtons["version"] = NavVersions;
        _navButtons["download"] = NavDownloads;
        _navButtons["server"] = NavServer;
        _navButtons["settings"] = NavSettings;
        // VM 到达后订阅激活态变化（覆盖点击/跳转/GoRepair 等所有导航路径）
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel main)
                main.PropertyChanged += OnVmPropertyChanged;
            ApplyNavVisuals();
        };
        // 窗口显示后 ActualTransparencyLevel 才为最终值；亚克力合成失败时切不透明，保证窗口永远可见
        Opened += (_, _) =>
        {
            RestoreWindowSize();
            ApplyOpacityFallback();
            ApplyAppearance();
            ApplyNavVisuals(); // 兜底：DataContext 若早于挂载，这里补一次
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

    // ---------- 导航视觉（本地值驱动；hover/按下/激活三态互斥恢复） ----------

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "IsHomeActive" or "IsVersionsActive" or "IsDownloadsActive" or "IsServerActive" or "IsSettingsActive")
            ApplyNavVisuals();
    }

    /// <summary>按 VM 激活态刷新全部导航视觉（active：深青底 + 白字 + 左侧 Accent 色条）</summary>
    private void ApplyNavVisuals()
    {
        if (DataContext is not MainViewModel main) return;
        foreach (var (page, btn) in _navButtons)
        {
            if (IsPageActive(main, page))
            {
                btn.Background = new SolidColorBrush(Color.Parse("#12332F"));
                btn.Foreground = Brushes.White;
                btn.BorderBrush = new SolidColorBrush(Color.Parse("#2DD4BF")); // Accent 左色条
                btn.BorderThickness = new Thickness(3, 0, 0, 0);
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = new SolidColorBrush(Color.Parse("#8A93A6")); // TextSecondary
                btn.BorderThickness = new Thickness(0);
            }
        }
    }

    private static bool IsPageActive(MainViewModel main, string page) => page switch
    {
        "home" => main.IsHomeActive,
        "version" => main.IsVersionsActive,
        "download" => main.IsDownloadsActive,
        "server" => main.IsServerActive,
        "settings" => main.IsSettingsActive,
        _ => false,
    };

    private void NavEnter(object? sender, PointerEventArgs e)
    {
        if (sender is not Button btn || btn is null || DataContext is not MainViewModel main) return;
        foreach (var (page, b) in _navButtons)
        {
            if (ReferenceEquals(b, btn) && IsPageActive(main, page)) return; // 激活项 hover 不改色
        }
        btn.Background = new SolidColorBrush(Color.Parse("#2C3544")); // BgHover 悬浮变色
        btn.Foreground = new SolidColorBrush(Color.Parse("#E8EAF0")); // TextPrimary
    }

    private void NavExit(object? sender, PointerEventArgs e) => ApplyNavVisuals();

    private void NavPress(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Button btn)
            btn.Background = new SolidColorBrush(Color.Parse("#1A2029")); // 按下变深，无白影（涟漪由 RippleBehavior 触发）
    }

    private void NavRelease(object? sender, PointerReleasedEventArgs e) => ApplyNavVisuals();

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
