using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Launcher.Animation;
using Launcher.App.ViewModels;
using Launcher.App.Views;
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

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // [生命周期引导] 注入 Avalonia 适配层
            AnimationService.UIAccessProviderFactory = () => new AvaloniaUIAccessProvider();
            LogService.FatalErrorReporter = message => System.Console.Error.WriteLine($"[FATAL] {message}");

            // 启动 PCL.Core 生命周期（Avalonia 驱动消息循环，不运行 WPF 容器）。
            // 任一环节失败只记日志，不得阻止窗口出现；窗口构造失败则仍为 fatal。
            Guard("Lifecycle.OnInitialize", () => Lifecycle.OnInitialize());

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };

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
