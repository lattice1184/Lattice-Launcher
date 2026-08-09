using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Launcher.App.Animations;
using Launcher.App.Services;
using Launcher.App.ViewModels;
using Launcher.Core.Utils;

namespace Launcher.App.Views;

public partial class MainWindow : Window
{
    /// <summary>导航按钮注册表（页面 id → 按钮）。视觉全部用本地值驱动——本地值优先级凌驾样式 Setter，
    /// 模板 TemplateBinding 实时跟随，根治样式伪类在 Avalonia 12 下 hover 失效 / pressed 白影的问题。</summary>
    private readonly Dictionary<string, Button> _navButtons = new();

    /// <summary>无边框窗口放大完成后恢复的常规尺寸下限（启动小窗下限由 XAML 设为 120）</summary>
    private const double NormalMinWidth = 760, NormalMinHeight = 500;

    /// <summary>激活指示条是否已首次定位（首次直接落位，之后切换才滑动）</summary>
    private bool _navIndicatorFirst = true;

    public MainWindow()
    {
        InitializeComponent();
        // splash 阶段窗口完全透明（logo 悬浮桌面）：隐藏亚克力层与描边，hint 先行切 Transparent
        // （Win11 WinUIComposition 支持；不支持则降级 AcrylicBlur/None，不算 bug）
        TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.Transparent,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.None,
        };
        RootSurface.IsVisible = false;
        WindowRoot.BorderBrush = Brushes.Transparent;
        _navButtons["home"] = NavHome;
        _navButtons["version"] = NavVersions;
        _navButtons["download"] = NavDownloads;
        _navButtons["server"] = NavServer;
        _navButtons["multiplayer"] = NavMultiplayer;
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
            UiAnim.Host = this; // 渲染帧驱动的全局宿主（splash 等无 host 参数的动画取帧时钟）
            ApplyOpacityFallback();
            ApplyAppearance(LauncherSettings.Current.WindowOpacity, LauncherSettings.Current.Density);
            ApplyNavVisuals(); // 兜底：DataContext 若早于挂载，这里补一次
            // 页面切换：平滑滑入淡出
            PageHost.PageTransition = new UiAnim.FadeSlideTransition { Duration = TimeSpan.FromMilliseconds(180) };
            // Toast 出现时右侧滑入（NVIDIA 浮窗风；Opacity 淡入淡出由绑定驱动），关闭时右滑出
            ToastsHost.ContainerPrepared += (_, e) =>
            {
                UiAnim.SlideInX(e.Container);
                if (e.Container.DataContext is ToastItem t)
                    t.OnRemoving = () => UiAnim.SlideOutToRight(e.Container);
            };
            // 外观实时跟随设置页改动（保存应用 + 预览）。
            // AL7：预览必须传 VM 值——Settings 未写盘时读不到新值，旧版预览/即时生效永远不变化
            if (DataContext is MainViewModel main)
            {
                main.Settings.AppearanceChanged += () => ApplyAppearance(LauncherSettings.Current.WindowOpacity, LauncherSettings.Current.Density);
                main.Settings.PreviewChanged += () => ApplyAppearance(main.Settings.WindowOpacity, (DensityMode)main.Settings.DensityIndex);
            }
            // 启动序列：小窗(仅 logo) → logo 缩放出现 → 窗口从中心放大 + 内容涨开 + 浮层淡出（放在 ApplyAppearance 之后，密度基准已设）
            StartSplashSequence();
            // 彩蛋：启动完成后随机一条小提示（可关）
            if (LauncherSettings.Current.StartupTipEnabled)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1800); // 等放大/淡出播完
                    Dispatcher.UIThread.Post(() => NotificationService.Info(StartupTips.Random()));
                });
            }
        };
        // 无边框最大化：外框圆角归 0，避免透明角露出壁纸（监听 WindowState 属性变化）
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                WindowRoot.CornerRadius = WindowState == WindowState.Maximized ? new CornerRadius(0) : new CornerRadius(12);
        };
        Closing += (_, _) => SaveWindowSize();
    }

    /// <summary>logo 缩放变换（ScaleTransform 无 x:Name 字段，运行时从 RenderTransform 取）</summary>
    private ScaleTransform SplashLogoS => (ScaleTransform)SplashLogo.RenderTransform;

    /// <summary>启动序列：logo 从透明中浮现放大（Decelerate 无过冲、慢起快收——落定速度≈0）→ 窗口放大段同样慢起，速度连续无割裂。</summary>
    private void StartSplashSequence()
    {
        SplashLogoS.ScaleX = SplashLogoS.ScaleY = 0.3;
        SplashLogo.Opacity = 0;
        UiAnim.Animate(450, UiAnim.Curves.Decelerate, e =>
        {
            SplashLogoS.ScaleX = 0.3 + 0.7 * e;
            SplashLogoS.ScaleY = 0.3 + 0.7 * e;
            SplashLogo.Opacity = e;
        }, GrowToFull, UiAnim.Host);
    }

    /// <summary>窗口实时放大：150×150 → 存档尺寸（逐帧居中），内容从中心 0.25→1 涨开，浮层后段淡出（logo 随窗口"长成"界面）。</summary>
    private void GrowToFull()
    {
        var (targetW, targetH) = ResolveTargetSize();
        var startW = Width;
        var startH = Height;
        var wa = Screens.Primary?.WorkingArea;
        var scale = RenderScaling > 0 ? RenderScaling : 1.0;
        var densityBase = ContentSurface?.RenderTransform is ScaleTransform d ? d.ScaleX : 1.0;

        // 与阶段 1 同款 decelerate：窗口放大慢起（与 logo 落定速度连续）、尾段快收
        UiAnim.Animate(950, UiAnim.Curves.Decelerate, e =>
        {
            var w = startW + (targetW - startW) * e;
            var h = startH + (targetH - startH) * e;
            Width = w;
            Height = h;
            if (wa is { } a)
                Position = new PixelPoint(a.X + (int)((a.Width - w * scale) / 2), a.Y + (int)((a.Height - h * scale) / 2));
            // 内容从中心涨开（以密度缩放为基准）
            if (ContentSurface?.RenderTransform is ScaleTransform st)
            {
                st.ScaleX = densityBase * (0.25 + 0.75 * e);
                st.ScaleY = densityBase * (0.25 + 0.75 * e);
            }
            // logo 随窗口放大；交叉淡化并入同一帧时钟：浮层 e>0.45 淡出、内容 e>0.55 淡入（重叠充分，无空窗）
            SplashLogoS.ScaleX = SplashLogoS.ScaleY = 1 + 0.6 * e;
            SplashOverlay.Opacity = e < 0.45 ? 1 : 1 - (e - 0.45) / 0.55;
            AppContent.Opacity = e < 0.55 ? 0 : (e - 0.55) / 0.45;
        }, () =>
        {
            MinWidth = NormalMinWidth;
            MinHeight = NormalMinHeight;
            SplashOverlay.Opacity = 1;
            SplashOverlay.IsVisible = false;
            SplashLogoS.ScaleX = SplashLogoS.ScaleY = 1;
            ApplyAppearance(LauncherSettings.Current.WindowOpacity, LauncherSettings.Current.Density); // 复位密度缩放
            // 放大完成：切回液态玻璃——亚克力层与描边 220ms 渐进出现（替代瞬间切换）
            WindowRoot.BorderBrush = Brushes.Transparent;
            RootSurface.Opacity = 0;
            RootSurface.IsVisible = true;
            TransparencyLevelHint = new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None,
            };
            UiAnim.Animate(UiAnim.Durations.Standard, UiAnim.Curves.Standard, e =>
            {
                RootSurface.Opacity = e;
                WindowRoot.BorderBrush = LerpBrush(Colors.Transparent, Color.Parse("#4D2F3745"), e);
            }, null, RootSurface);
            // 兜底：splash 期间导航未布局被跳过，RootSurface 可见后 150ms 一次性补定位（不用链式 Post，防饿死 UI 线程）
            var indicatorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            indicatorTimer.Tick += (_, _) => { indicatorTimer.Stop(); ApplyNavVisuals(); };
            indicatorTimer.Start();
        }, UiAnim.Host);
    }

    /// <summary>颜色线性插值（亚克力层淡入时描边同步渐显；不依赖 Color.Lerp 的版本差异）</summary>
    private static IBrush LerpBrush(Color from, Color to, double e)
    {
        var c = Color.FromArgb(
            (byte)(from.A + (to.A - from.A) * e),
            (byte)(from.R + (to.R - from.R) * e),
            (byte)(from.G + (to.G - from.G) * e),
            (byte)(from.B + (to.B - from.B) * e));
        return new SolidColorBrush(c);
    }

    /// <summary>放大目标 = 存档窗口尺寸（夹到主屏工作区内）；无存档用默认 860×560。</summary>
    private (double w, double h) ResolveTargetSize()
    {
        var s = LauncherSettings.Current;
        if (Screens.Primary?.WorkingArea is not { } wa) return (900, 600);
        var w = s.WindowWidth >= NormalMinWidth ? Math.Min(s.WindowWidth, wa.Width) : 900.0;
        var h = s.WindowHeight >= NormalMinHeight ? Math.Min(s.WindowHeight, wa.Height) : 600.0;
        return (w, h);
    }

    /// <summary>关闭时记住窗口尺寸（下次启动时由 GrowToFull 放大到该尺寸）。
    /// 防再污染：最大化/最小化时不覆盖存档；尺寸夹到主屏工作区 95% 内（防止真机测试/改分辨率
    /// 把超大尺寸存进设置，导致下次启动「窗口突然变大」）。</summary>
    private void SaveWindowSize()
    {
        var s = LauncherSettings.Current;
        if (WindowState != WindowState.Normal) return;
        var wa = Screens.Primary?.WorkingArea;
        s.WindowWidth = wa is { } a ? Math.Min(Width, a.Width * 0.95) : Width;
        s.WindowHeight = wa is { } b ? Math.Min(Height, b.Height * 0.95) : Height;
        s.Save();
    }

    // ---------- 无边框窗口标题栏：拖拽 / 双击最大化 / 最小化 / 最大化 / 关闭 ----------

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount >= 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        BeginMoveDrag(e);
    }

    // ---------- 窗口按钮（纯 Border，手动 hover 变色；无模板/行为，杜绝缩放位移错位） ----------

    private void TitleBtn_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Border b) return;
        if (ReferenceEquals(b, BtnClose))
        {
            b.Background = new SolidColorBrush(Color.Parse("#C42B1C"));
            BtnCloseGlyph.Foreground = Brushes.White;
        }
        else
        {
            b.Background = new SolidColorBrush(Color.Parse("#2C3544"));
            BtnMinGlyph.Foreground = Brushes.White;
        }
    }

    private void TitleBtn_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is not Border b) return;
        b.Background = Brushes.Transparent;
        if (ReferenceEquals(b, BtnClose)) BtnCloseGlyph.Foreground = new SolidColorBrush(Color.Parse("#8A93A6"));
        else BtnMinGlyph.Foreground = new SolidColorBrush(Color.Parse("#8A93A6"));
    }

    private void TitleBtn_PointerPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true; // 挡住标题条拖拽

    private void TitleBtn_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border b || !b.IsPointerOver) return;
        if (ReferenceEquals(b, BtnClose)) Close();
        else WindowState = WindowState.Minimized;
    }

    // ---------- 导航视觉（本地值驱动；hover/按下/激活三态互斥恢复） ----------

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "IsHomeActive" or "IsVersionsActive" or "IsDownloadsActive" or "IsServerActive" or "IsMultiplayerActive" or "IsSettingsActive")
            ApplyNavVisuals();
    }

    /// <summary>
    /// 自定义背景：内容区铺满图片 + 65% 黑压暗（保文字可读）。
    /// 空路径还原默认半透明背板（#B81D222C）；图片失效（被删/损坏）回退默认，不崩。
    /// 内存：DecodeToWidth 限 2560 宽；换图前释放旧 Bitmap。
    /// </summary>
    public void ApplyBackgroundImage(string? path)
    {
        // 释放旧图（若有）
        if (ContentSurface.Background is ImageBrush { Source: Bitmap old })
        {
            old.Dispose();
            ContentSurface.Background = null;
        }
        var fallback = new SolidColorBrush(Color.Parse("#B81D222C"));
        if (string.IsNullOrWhiteSpace(path))
        {
            ContentSurface.Background = fallback;
            BgDim.IsVisible = false;
            return;
        }
        try
        {
            var bitmap = new Bitmap(path);
            ContentSurface.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            BgDim.IsVisible = true;
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"[BACKGROUND] 背景图片加载失败 {path}: {ex.Message}");
            ContentSurface.Background = fallback;
            BgDim.IsVisible = false;
        }
    }

    /// <summary>按 VM 激活态刷新全部导航视觉（active：深青底 + 白字；左色条由独立元素滑动定位）</summary>
    private void ApplyNavVisuals()
    {
        if (DataContext is not MainViewModel main) return;
        foreach (var (page, btn) in _navButtons)
        {
            if (IsPageActive(main, page))
            {
                // 主题系统：激活底跟随 AccentDark 派生色（ApplyAccentColor 已写入；缺 key 兜底默认）
                btn.Background = new SolidColorBrush(
                    App.Current.Resources.TryGetResource("AccentDark", null, out var dark) && dark is Color dc
                        ? dc
                        : Color.Parse("#12332F"));
                btn.Foreground = Brushes.White;
                MoveNavIndicator(btn);
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = new SolidColorBrush(Color.Parse("#8A93A6")); // TextSecondary
            }
        }
    }

    /// <summary>激活指示条滑到目标按钮：首次直接定位，之后 180ms 平滑滑动（host=Indicator 互斥打断连点）。
    /// AL7：强调色跟随设置；按钮位置/高度用 TranslatePoint 实测，不假设布局常量。
    /// 注意：布局未就绪（splash 期间 RootSurface 不可见，子树不布局）时直接跳过——绝不 Post 重试
    /// （会每帧往 UI 队列塞回调把渲染饿死 → 未响应），定位兜底在 splash 完成回调里做。</summary>
    private void MoveNavIndicator(Button btn)
    {
        var accentHex = LauncherSettings.Current.AccentColor;
        NavIndicator.Background = new SolidColorBrush(Color.Parse(
            string.IsNullOrWhiteSpace(accentHex) || !accentHex.StartsWith('#') ? "#2DD4BF" : accentHex));
        if (NavIndicator.Parent is not Visual parent) return;
        if (btn.Bounds.Height <= 0) return; // 布局未就绪，跳过（splash 完成后由兜底调用补齐）
        var top = btn.TranslatePoint(new Point(0, 0), parent)?.Y ?? NavIndicator.Margin.Top;
        var h = btn.Bounds.Height;
        if (_navIndicatorFirst)
        {
            _navIndicatorFirst = false;
            NavIndicator.Height = h;
            NavIndicator.Margin = new Thickness(10, top, 0, 0);
            return;
        }
        var fromTop = NavIndicator.Margin.Top;
        var fromH = NavIndicator.Height;
        UiAnim.Animate(180, UiAnim.Curves.Standard, e =>
        {
            NavIndicator.Margin = new Thickness(10, fromTop + (top - fromTop) * e, 0, 0);
            NavIndicator.Height = fromH + (h - fromH) * e;
        }, null, NavIndicator);
    }

    private static bool IsPageActive(MainViewModel main, string page) => page switch
    {
        "home" => main.IsHomeActive,
        "version" => main.IsVersionsActive,
        "download" => main.IsDownloadsActive,
        "server" => main.IsServerActive,
        "multiplayer" => main.IsMultiplayerActive,
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

    /// <summary>应用外观设置：窗口透明度 + 界面密度（强调色由 App 应用）。
    /// AL7：参数化——预览传 VM 值（未写盘），保存传 Settings 值。</summary>
    private void ApplyAppearance(double opacity, DensityMode density)
    {
        // 透明度：亚克力 TintOpacity 随设置（0.7-1.0 → 0.40-1.0 映射）
        if (RootSurface?.Material is ExperimentalAcrylicMaterial m)
        {
            m.TintOpacity = 0.40 + (opacity - 0.7) * 2.0; // 0.7→0.40，1.0→1.0
        }

        // 密度：整 UI 缩放（AL7 上调：紧凑 0.95 / 标准 1.0 / 舒适 1.15——旧 0.9 默认把整 UI 缩 10%，字太小主因）
        if (ContentSurface?.RenderTransform is Avalonia.Media.ScaleTransform scaleTransform)
        {
            var scale = density switch
            {
                DensityMode.Compact => 0.95,
                DensityMode.Comfortable => 1.15,
                _ => 1.0,
            };
            scaleTransform.ScaleX = scale;
            scaleTransform.ScaleY = scale;
        }
    }
}
