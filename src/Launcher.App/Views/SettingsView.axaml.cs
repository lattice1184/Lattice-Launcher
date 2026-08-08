using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Launcher.App.Animations;
using Launcher.Core.Utils;

namespace Launcher.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        // 汉堡菜单锚定到 ☰ 按钮（代码赋值比 XAML 元素绑定稳）；默认显示"游戏目录"分区
        SettingsMenu.PlacementTarget = SettingsMenuButton;
        ShowSection(0);
    }

    // ---------- 分类菜单（汉堡按钮弹出；本地值驱动视觉，防 Avalonia 12 伪类不可靠 / hover 错位） ----------

    private int _activeSection;

    private void OnToggleMenu(object? sender, RoutedEventArgs e)
    {
        if (SettingsMenu.IsOpen) CloseMenuAnimated(); // 收起先播动画再关
        else SettingsMenu.IsOpen = true;              // 弹出由 Opened 事件弹入
    }

    /// <summary>☰ 菜单弹入：缩放 0.9→1 + 淡入（180ms Standard，无弹性过冲——host=child 互斥打断连点重播）</summary>
    private void OnSettingsMenuOpened(object? sender, EventArgs e)
    {
        if (SettingsMenu.Child is not Control child) return;
        child.Opacity = 0;
        var tx = new ScaleTransform(0.9, 0.9);
        child.RenderTransform = tx;
        UiAnim.Animate(180, UiAnim.Curves.Standard, e2 =>
        {
            child.Opacity = e2;
            tx.ScaleX = 0.9 + 0.1 * e2;
            tx.ScaleY = 0.9 + 0.1 * e2;
        }, null, child);
    }

    /// <summary>☰ 菜单收起：先反向缩放+淡出（120ms），done 后才关。起点取当前值（弹入被中断时无跳变）。
    /// 点击外部 dismiss（IsLightDismissEnabled）是系统行为无法拦截，保持瞬间关闭。</summary>
    private void CloseMenuAnimated()
    {
        if (!SettingsMenu.IsOpen || SettingsMenu.Child is not Control child) return;
        var fromO = child.Opacity;
        var tx = child.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        var fromS = tx.ScaleX;
        child.RenderTransform = tx;
        UiAnim.Animate(120, UiAnim.Curves.Standard, e =>
        {
            child.Opacity = fromO * (1 - e);
            tx.ScaleX = fromS + (0.9 - fromS) * e;
            tx.ScaleY = tx.ScaleX;
        }, () => SettingsMenu.IsOpen = false, child);
    }

    private void OnSettingsNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string idx } && int.TryParse(idx, out var i))
            ShowSection(i);
    }

    /// <summary>分类切换：ContentControl 覆盖式布局——直接替换内容 + 新分区淡入上移（200ms），
    /// 旧分区瞬间消失即可（无流布局占位问题）。首帧（尚未有内容）直接显示不动画。</summary>
    private void ShowSection(int index)
    {
        _activeSection = index;
        ApplySettingsNavVisuals();
        CloseMenuAnimated(); // 选完自动收起（先播动画再关）
        var content = BuildSection(index);
        if (ContentHost.Content is null) { ContentHost.Content = content; return; }
        content.Opacity = 0;
        var ty = new TranslateTransform(0, 8);
        content.RenderTransform = ty;
        ContentHost.Content = content;
        UiAnim.Animate(200, UiAnim.Curves.Standard, e =>
        {
            content.Opacity = e;
            ty.Y = 8 * (1 - e);
        }, () => content.RenderTransform = null, content); // done 清残留变换
    }

    private static Control BuildSection(int index) => index switch
    {
        0 => new SectionGameDirView(),
        1 => new SectionLaunchView(),
        2 => new SectionAppearanceView(),
        3 => new SectionDownloadView(),
        _ => new SectionAboutView(),
    };

    private void ApplySettingsNavVisuals()
    {
        var accent = AccentBrush();
        SetNavVisual(SettingsNavGameDir, _activeSection == 0, accent);
        SetNavVisual(SettingsNavLaunch, _activeSection == 1, accent);
        SetNavVisual(SettingsNavAppearance, _activeSection == 2, accent);
        SetNavVisual(SettingsNavDownload, _activeSection == 3, accent);
        SetNavVisual(SettingsNavAbout, _activeSection == 4, accent);
    }

    private static void SetNavVisual(Button btn, bool active, IBrush accent)
    {
        btn.Background = active ? new SolidColorBrush(Color.Parse("#12332F")) : Brushes.Transparent;
        btn.Foreground = active ? Brushes.White : new SolidColorBrush(Color.Parse("#8A93A6"));
        btn.BorderBrush = active ? accent : Brushes.Transparent;
        btn.BorderThickness = active ? new Thickness(3, 0, 0, 0) : new Thickness(0);
    }

    private static IBrush AccentBrush()
    {
        var hex = LauncherSettings.Current.AccentColor;
        return new SolidColorBrush(Color.Parse(string.IsNullOrWhiteSpace(hex) || !hex.StartsWith('#') ? "#2DD4BF" : hex));
    }

    private bool IsActiveNav(Button btn) =>
        ReferenceEquals(btn, SettingsNavGameDir) && _activeSection == 0
        || ReferenceEquals(btn, SettingsNavLaunch) && _activeSection == 1
        || ReferenceEquals(btn, SettingsNavAppearance) && _activeSection == 2
        || ReferenceEquals(btn, SettingsNavDownload) && _activeSection == 3
        || ReferenceEquals(btn, SettingsNavAbout) && _activeSection == 4;

    private void SettingsNavEnter(object? sender, PointerEventArgs e)
    {
        if (sender is not Button btn) return;
        if (ReferenceEquals(btn, SettingsMenuButton))
        {
            btn.Background = new SolidColorBrush(Color.Parse("#2C3544")); // ☰ 触发钮无激活态，hover 直接变灰
            return;
        }
        if (IsActiveNav(btn)) return; // 激活项 hover 不改色
        btn.Background = new SolidColorBrush(Color.Parse("#2C3544"));
        btn.Foreground = new SolidColorBrush(Color.Parse("#E8EAF0"));
    }

    private void SettingsNavExit(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(sender, SettingsMenuButton))
            SettingsMenuButton.Background = Brushes.Transparent;
        else
            ApplySettingsNavVisuals();
    }

    private void SettingsNavPress(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Button btn) btn.Background = new SolidColorBrush(Color.Parse("#1A2029"));
    }

    private void SettingsNavRelease(object? sender, PointerReleasedEventArgs e)
    {
        if (ReferenceEquals(sender, SettingsMenuButton))
            SettingsMenuButton.Background = new SolidColorBrush(Color.Parse("#2C3544")); // 松手仍悬停 → 回到 hover 色
        else
            ApplySettingsNavVisuals();
    }
}
