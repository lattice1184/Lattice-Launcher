using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Launcher.Animation;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core.Utils;
using PCL.Core.App.IoC;
using PCL.Core.Logging;
using PCL.Core.UI.Animation.Core;

namespace Launcher.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 主窗口关闭前不退出
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // 启动浮层在主窗口内部（AL16：避免独立 splash 窗口关窗/开窗的强切，过渡连续可见）
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
            // 外观实时应用：保存（AppearanceChanged）与预览（PreviewChanged）都刷新强调色 + 自定义背景。
            // AL7：预览必须传 VM 值（Settings 未写盘时读不到新值——旧版预览永远不生效）
            if (desktop.MainWindow.DataContext is MainViewModel mainVm)
            {
                var window = desktop.MainWindow as MainWindow;
                mainVm.Settings.AppearanceChanged += () =>
                {
                    ApplyAccentColor(LauncherSettings.Current.AccentColor);
                    window?.ApplyBackgroundImage(LauncherSettings.Current.BackgroundImagePath);
                };
                mainVm.Settings.PreviewChanged += () =>
                {
                    ApplyAccentColor(mainVm.Settings.AccentColor);
                    window?.ApplyBackgroundImage(mainVm.Settings.BackgroundImagePathText);
                };
            }
            // 启动序列在 Opened 里触发（小窗 logo → 窗口放大）；这里同步做初始化，任一失败只记日志不阻止窗口出现
            // 启动时确保自建游戏目录结构（D 盘优先；无 D 盘回退 Downloads\YanKa Launcher\.minecraft）
            Guard("GameDirectory.EnsureDefault", GameDirectory.EnsureDefault);

            // 应用个性化强调色与自定义背景（设置页可改，运行时可换）
            ApplyAccentColor(LauncherSettings.Current.AccentColor);
            (desktop.MainWindow as MainWindow)?.ApplyBackgroundImage(LauncherSettings.Current.BackgroundImagePath);

            // [生命周期引导] 注入 Avalonia 适配层
            AnimationService.UIAccessProviderFactory = () => new AvaloniaUIAccessProvider();
            LogService.FatalErrorReporter = message => ShowFatalError(message);

            // 启动 PCL.Core 生命周期（Avalonia 驱动消息循环，不运行 WPF 容器）。
            // 任一环节失败只记日志，不得阻止窗口出现；窗口构造失败则仍为 fatal。
            Guard("Lifecycle.OnInitialize", () => Lifecycle.OnInitialize());

            // Show → Opened → StartSplashSequence（小窗 logo 缩放出现 → 窗口放大到正式页面）
            desktop.MainWindow.Show();

            // 首次启动询问游戏目录（settings.json 未指定时），确认后写入，之后不再询问
            if (LauncherSettings.Current.GameDirectory is null)
            {
                try { await new GameDirSetupWindow().ShowDialog(desktop.MainWindow); }
                catch (Exception ex) { System.Console.Error.WriteLine($"[FATAL] GameDirSetupWindow: {ex}"); }
            }

            Guard("Lifecycle.OnLoading", () => Lifecycle.OnLoading());
            Guard("Lifecycle.OnWindowCreated", () => Lifecycle.OnWindowCreated());
            desktop.Exit += (_, _) => Guard("Lifecycle.Shutdown", () => Lifecycle.Shutdown());
            // UI 线程未捕获异常兜底（弹崩溃窗口 + 防崩溃）
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                var msg = e.Exception?.Message ?? "";
                // 布局/渲染阶段异常不置 Handled：半坏状态继续会连环出错，交给进程崩溃兜底并保留堆栈
                if (msg.Contains("Layout") || msg.Contains("Arrange") || msg.Contains("Measure") || msg.Contains("Render"))
                {
                    ShowFatalError($"界面异常：{e.Exception}");
                    return;
                }
                e.Handled = true;
                ShowFatalError($"未捕获异常：{e.Exception}");
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static int _fatalShown;

    /// <summary>致命错误：写日志 + 弹崩溃报告窗口（PCL 式；防递归只弹一次）</summary>
    private static void ShowFatalError(string message)
    {
        System.Console.Error.WriteLine($"[FATAL] {message}");
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log"),
                $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* 日志写入失败不阻塞 */ }

        try
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Interlocked.Exchange(ref _fatalShown, 1) == 1) return; // 只弹一次（展示时才置位）
                try
                {
                    if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                        Views.CrashReportWindow.Show(message);
                    else
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Views.CrashReportWindow.Show(message));
                }
                catch { /* 弹窗失败不递归 */ }
            });
        }
        catch { }
    }

    /// <summary>
    /// 应用强调色（主题系统）：替换 Accent/AccentHover 及派生色 AccentDark（深卡）/AccentLight（亮字）/
    /// AccentSoft（半透明深卡）/OnAccent（前景对比色）——按钮/进度条/tab/卡片徽章全跟随。
    /// </summary>
    private void ApplyAccentColor(string hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith('#')) hex = "#2DD4BF";
            var accent = Avalonia.Media.Color.Parse(hex);
            Resources["Accent"] = accent;
            // AccentHover = 每通道提亮 8%
            var h = accent;
            h = new Avalonia.Media.Color(h.A,
                (byte)Math.Min(255, h.R + 255 * 0.08),
                (byte)Math.Min(255, h.G + 255 * 0.08),
                (byte)Math.Min(255, h.B + 255 * 0.08));
            Resources["AccentHover"] = h;
            // 派生色（纯字节数学，Core 可测）
            var rgb = AccentColorMath.TryNormalizeHex(hex) ?? new Rgb24(0x2D, 0xD4, 0xBF);
            var dark = AccentColorMath.DeriveDark(rgb);
            Resources["AccentDark"] = Avalonia.Media.Color.FromRgb(dark.R, dark.G, dark.B);
            Resources["AccentSoft"] = Avalonia.Media.Color.FromArgb(AccentColorMath.SoftAlpha, dark.R, dark.G, dark.B);
            var light = AccentColorMath.DeriveLight(rgb);
            Resources["AccentLight"] = Avalonia.Media.Color.FromRgb(light.R, light.G, light.B);
            var on = AccentColorMath.DeriveOnAccent(rgb);
            Resources["OnAccent"] = Avalonia.Media.Color.FromRgb(on.R, on.G, on.B);
        }
        catch { /* 强调色非法则保持默认 */ }
    }

    /// <summary>生命周期调用兜底：异常只记录，不阻止窗口创建</summary>
    private static void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { System.Console.Error.WriteLine($"[FATAL] {what}: {ex}"); }
    }
}
