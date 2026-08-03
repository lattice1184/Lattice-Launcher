using Avalonia;
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
            // 启动时确保自建游戏目录结构（D 盘优先；无 D 盘回退 Downloads\YanKa Launcher\.minecraft）
            Guard("GameDirectory.EnsureDefault", GameDirectory.EnsureDefault);

            // [生命周期引导] 注入 Avalonia 适配层
            AnimationService.UIAccessProviderFactory = () => new AvaloniaUIAccessProvider();
            LogService.FatalErrorReporter = message =>
            {
                System.Console.Error.WriteLine($"[FATAL] {message}");
                try
                {
                    var logDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "logs");
                    Directory.CreateDirectory(logDir);
                    File.AppendAllText(Path.Combine(logDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt"),
                        $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                }
                catch { /* 日志写入失败不阻塞 */ }
            };

            // 启动 PCL.Core 生命周期（Avalonia 驱动消息循环，不运行 WPF 容器）。
            // 任一环节失败只记日志，不得阻止窗口出现；窗口构造失败则仍为 fatal。
            Guard("Lifecycle.OnInitialize", () => Lifecycle.OnInitialize());

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
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
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>生命周期调用兜底：异常只记录，不阻止窗口创建</summary>
    private static void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { System.Console.Error.WriteLine($"[FATAL] {what}: {ex}"); }
    }
}
