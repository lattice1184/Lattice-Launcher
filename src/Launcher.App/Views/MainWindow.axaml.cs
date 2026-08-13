using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
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
        // AL47 整合包拖入：全窗口接收 zip/mrpack 拖拽（DragOver 过滤，Drop 取第一个导入）
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
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
            // 8-13 批次 33：窗口以目标尺寸铺开（透明，内容由独立 SplashWindow 罩着）→ 首帧后 150ms 淡入
            // （与 splash 淡出交叉——AL16 强切顾虑的解法）。不做窗口 resize 动画：
            // 透明窗口逐帧 SetWindowPos 走软件合成路径（实测只有几帧），窗口不动的纯内部动画
            // （ScaleTransform/Opacity）走 GPU 合成，帧率满。
            var (tw, th) = ResolveTargetSize();
            MinWidth = NormalMinWidth;
            MinHeight = NormalMinHeight;
            Width = tw;
            Height = th;
            if (Screens.Primary?.WorkingArea is { } a)
            {
                var scale = RenderScaling > 0 ? RenderScaling : 1.0;
                Position = new PixelPoint(a.X + (int)((a.Width - tw * scale) / 2), a.Y + (int)((a.Height - th * scale) / 2));
            }
            FadeInContent();
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

    /// <summary>8-13 批次 33 内容淡入：背景与内容同步 150ms 淡入（与独立 splash 的 150ms 淡出交叉——无强切）。
    /// 全程只写 Opacity/画刷，纯 GPU 合成。完成后切回液态玻璃合成级别 + 补一次导航指示条定位。</summary>
    private void FadeInContent()
    {
        RootSurface.IsVisible = true;
        UiAnim.Animate(UiAnim.Durations.Fast, UiAnim.Curves.Decelerate, e =>
        {
            AppContent.Opacity = e;
            RootSurface.Opacity = e;
            WindowRoot.BorderBrush = LerpBrush(Colors.Transparent, Color.Parse("#4D2F3745"), e);
        }, () =>
        {
            AppContent.Opacity = 1;
            // 切回液态玻璃：背景与描边已铺满（Opacity=1），这里只切合成级别
            WindowRoot.BorderBrush = LerpBrush(Colors.Transparent, Color.Parse("#4D2F3745"), 1);
            TransparencyLevelHint = new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None,
            };
            // 兜底：启动期间导航未布局被跳过，RootSurface 可见后 150ms 一次性补定位（不用链式 Post，防饿死 UI 线程）
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

    // ---------- 整合包拖入（AL47：zip/mrpack 全窗口可拖；Avalonia 12 走 DataTransfer.Items + DataFormat.File） ----------

    private static bool IsPackFile(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase);

    /// <summary>从拖拽项取文件路径（TryGetRaw 返回 IStorageItem 或原始路径字符串，兼容两种平台实现）</summary>
    private static string? TryGetFilePath(IDataTransferItem item)
    {
        if (!item.Formats.Contains(DataFormat.File)) return null;
        var raw = item.TryGetRaw(DataFormat.File);
        return raw switch
        {
            Avalonia.Platform.Storage.IStorageItem si => si.Path.LocalPath,
            string s => s,
            _ => null,
        };
    }

    private static void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        var hasPack = (e.DataTransfer.Items ?? []).Any(i => TryGetFilePath(i) is { } p && IsPackFile(p));
        e.DragEffects = hasPack ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        var packs = (e.DataTransfer.Items ?? [])
            .Select(TryGetFilePath)
            .Where(p => p is not null && IsPackFile(p))
            .Cast<string>()
            .ToList();
        if (packs.Count == 0)
        {
            NotificationService.Info("仅支持拖入 .zip / .mrpack 整合包文件");
            return;
        }
        if (packs.Count > 1)
            NotificationService.Info("已开始导入第一个整合包，其余已忽略");
        ModpackImportFlow.StartAsync(packs[0]);
    }

    // ---------- 窗口按钮（纯 Border，手动 hover 变色；无模板/行为，杜绝缩放位移错位） ----------

    private void TitleBtn_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Border b) return;
        if (ReferenceEquals(b, BtnClose))
        {
            UiAnim.TweenBrush(b, TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#C42B1C")), UiAnim.Durations.Fast, "nav");
            UiAnim.TweenBrush(BtnCloseGlyph, TextBlock.ForegroundProperty, Brushes.White, UiAnim.Durations.Fast, "nav");
        }
        else
        {
            UiAnim.TweenBrush(b, TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#2C3544")), UiAnim.Durations.Fast, "nav");
            UiAnim.TweenBrush(BtnMinGlyph, TextBlock.ForegroundProperty, Brushes.White, UiAnim.Durations.Fast, "nav");
        }
    }

    private void TitleBtn_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is not Border b) return;
        UiAnim.TweenBrush(b, TemplatedControl.BackgroundProperty, Brushes.Transparent, UiAnim.Durations.Fast, "nav");
        if (ReferenceEquals(b, BtnClose))
            UiAnim.TweenBrush(BtnCloseGlyph, TextBlock.ForegroundProperty, new SolidColorBrush(Color.Parse("#8A93A6")), UiAnim.Durations.Fast, "nav");
        else
            UiAnim.TweenBrush(BtnMinGlyph, TextBlock.ForegroundProperty, new SolidColorBrush(Color.Parse("#8A93A6")), UiAnim.Durations.Fast, "nav");
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
    /// 空路径清本地值 → 回落用户背景色（{DynamicResource BackgroundColor}）；图片失效（被删/损坏）同样回落，不崩。
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
        if (string.IsNullOrWhiteSpace(path))
        {
            // 无图 → 清本地值，让 {DynamicResource BackgroundColor}（用户背景色）生效
            ContentSurface.Background = null;
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
            ContentSurface.Background = null;
            BgDim.IsVisible = false;
        }
    }

    /// <summary>按 VM 激活态刷新全部导航视觉（active：深青底 + 白字；左色条由独立元素滑动定位）。
    /// 激活切换保持瞬跳——过渡由 NavIndicator 滑动承担；hover/释放的平滑走 TweenNavBack。</summary>
    private void ApplyNavVisuals()
    {
        if (DataContext is not MainViewModel main) return;
        foreach (var (page, btn) in _navButtons)
        {
            var (bg, fg) = NavTargetVisuals(btn);
            btn.Background = bg;
            btn.Foreground = fg;
            if (IsPageActive(main, page)) MoveNavIndicator(btn);
        }
    }

    /// <summary>导航按钮目标视觉（激活/非激活）——ApplyNavVisuals 瞬跳与 NavExit/NavRelease 过渡共用，
    /// 避免两处颜色计算漂移。激活底跟随 AccentDark 派生色（主题系统）。</summary>
    private (IBrush Bg, IBrush Fg) NavTargetVisuals(Button btn)
    {
        foreach (var (page, b) in _navButtons)
        {
            if (ReferenceEquals(b, btn) && DataContext is MainViewModel main && IsPageActive(main, page))
            {
                var bg = App.Current.Resources.TryGetResource("AccentDark", null, out var dark) && dark is Color dc
                    ? (IBrush)new SolidColorBrush(dc)
                    : (IBrush)new SolidColorBrush(Color.Parse("#12332F"));
                return (bg, Brushes.White);
            }
        }
        return (Brushes.Transparent, new SolidColorBrush(Color.Parse("#8A93A6"))); // TextSecondary
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
        // 布局写一次落定（Margin/Height），动画期用 Transform 反向补偿——每帧 0 布局失效。
        // 打断重入：先捕获当前视觉态（Margin + 现存 Transform 的位移/缩放），再落定；
        // e=0 时补偿量=0 → 视觉位置=原处，e=1 时恒等 → done 清变换
        var fromTopEff = NavIndicator.Margin.Top;
        var fromHEff = NavIndicator.Height;
        if (NavIndicator.RenderTransform is TransformGroup g
            && g.Children[0] is TranslateTransform tt0 && g.Children[1] is ScaleTransform st0)
        {
            fromTopEff += tt0.Y;
            fromHEff *= st0.ScaleY;
        }
        NavIndicator.Margin = new Thickness(10, top, 0, 0);
        NavIndicator.Height = h;
        var tt = new TranslateTransform(0, 0);
        var st = new ScaleTransform(1, 1);
        NavIndicator.RenderTransform = new TransformGroup { Children = { tt, st } };
        NavIndicator.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative); // 顶部锚定，ScaleY 不位移
        UiAnim.Animate(180, UiAnim.Curves.Standard, e =>
        {
            tt.Y = (fromTopEff - top) * (1 - e);
            st.ScaleY = (fromHEff + (h - fromHEff) * e) / h;
        }, () => NavIndicator.RenderTransform = null, NavIndicator);
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
        UiAnim.TweenBrush(btn, TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#2C3544")), UiAnim.Durations.Fast, "nav"); // BgHover
        UiAnim.TweenBrush(btn, TemplatedControl.ForegroundProperty, new SolidColorBrush(Color.Parse("#E8EAF0")), UiAnim.Durations.Fast, "nav"); // TextPrimary
    }

    private void NavExit(object? sender, PointerEventArgs e) => TweenNavBack(sender);

    private void NavPress(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Button btn)
            UiAnim.TweenBrush(btn, TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#1A2029")), UiAnim.Durations.Fast, "nav"); // 按下变深
    }

    private void NavRelease(object? sender, PointerReleasedEventArgs e) => TweenNavBack(sender);

    /// <summary>悬停退出/松手释放：动画回激活态目标色（不再瞬跳）</summary>
    private void TweenNavBack(object? s)
    {
        if (s is not Button btn) return;
        var (bg, fg) = NavTargetVisuals(btn);
        UiAnim.TweenBrush(btn, TemplatedControl.BackgroundProperty, bg, UiAnim.Durations.Fast, "nav");
        UiAnim.TweenBrush(btn, TemplatedControl.ForegroundProperty, fg, UiAnim.Durations.Fast, "nav");
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
