using System;
using System.Windows;
using PCL.Core.App.IoC;

namespace PCL.Core.App.Essentials;

// [Avalonia 补丁] 禁用：WPF 窗口服务，Avalonia 宿主不使用
public sealed class MainWindowService : GeneralService
{
    public static Func<Window>? Loading { private get; set; }

    private static LifecycleContext? _context;
    private static LifecycleContext Context => _context!;
    private MainWindowService() : base("window", "主窗体", false) { _context = ServiceContext; }
    
    public override void Start()
    {
        Context.Debug("正在初始化 WPF 窗体");
        var window = Loading!.Invoke();
        window.Loaded += (_, _) => Lifecycle.OnWindowCreated();
        Lifecycle.CurrentApplication.MainWindow = window;
        Context.Trace("窗体创建完毕");
    }
}
