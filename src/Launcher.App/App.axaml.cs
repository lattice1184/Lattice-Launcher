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

            // 启动 PCL.Core 生命周期（Avalonia 驱动消息循环，不运行 WPF 容器）
            Lifecycle.OnInitialize();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };

            Lifecycle.OnLoading();
            Lifecycle.OnWindowCreated();
            desktop.Exit += (_, _) => Lifecycle.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
