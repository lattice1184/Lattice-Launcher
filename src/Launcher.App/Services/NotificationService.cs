using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.Services;

/// <summary>通知类型（决定提示点与边框颜色）</summary>
public enum ToastType { Info, Success, Error }

/// <summary>单条 Toast（右上角堆叠；Opacity 由服务控制，视图绑定过渡动画）</summary>
public partial class ToastItem : ObservableObject
{
    public string Message { get; }
    public ToastType Type { get; }

    public IBrush Dot => Type switch
    {
        ToastType.Success => new SolidColorBrush(Color.Parse("#5AD07C")),
        ToastType.Error => new SolidColorBrush(Color.Parse("#E05A5A")),
        _ => new SolidColorBrush(Color.Parse("#2DD4BF")),
    };

    public IBrush Border => Type switch
    {
        ToastType.Success => new SolidColorBrush(Color.Parse("#335AD07C")),
        ToastType.Error => new SolidColorBrush(Color.Parse("#33E05A5A")),
        _ => new SolidColorBrush(Color.Parse("#332DD4BF")),
    };

    /// <summary>透明度（视图 DoubleTransition 绑定；初始 0 淡入）</summary>
    [ObservableProperty]
    public partial double Opacity { get; set; } = 0;

    public ToastItem(string message, ToastType type)
    {
        Message = message;
        Type = type;
    }
}

/// <summary>
/// 全局通知服务：右上角 Toast 堆叠（成功/信息/错误），任意 VM 可调。
/// MainWindow 顶层覆盖层绑定 Toasts 集合展示。
/// </summary>
public static class NotificationService
{
    public static ObservableCollection<ToastItem> Toasts { get; } = [];

    /// <summary>弹一条 Toast（淡入 + 停留 + 淡出，自动移除）</summary>
    public static void Show(string message, ToastType type = ToastType.Info, int durationMs = 3200)
    {
        var toast = new ToastItem(message, type);
        if (Dispatcher.UIThread.CheckAccess()) Toasts.Add(toast);
        else Dispatcher.UIThread.Post(() => Toasts.Add(toast));
        // 淡入（DoubleTransition 平滑；先排队到集合添加之后）
        Dispatcher.UIThread.Post(() => toast.Opacity = 1);
        _ = FadeOutAsync(toast, durationMs);
    }

    /// <summary>信息提示快捷方式</summary>
    public static void Info(string message, int durationMs = 3600) => Show(message, ToastType.Info, durationMs);

    /// <summary>成功提示快捷方式</summary>
    public static void Success(string message, int durationMs = 3200) => Show(message, ToastType.Success, durationMs);

    /// <summary>错误提示快捷方式</summary>
    public static void Error(string message, int durationMs = 4500) => Show(message, ToastType.Error, durationMs);

    private static async Task FadeOutAsync(ToastItem toast, int durationMs)
    {
        await Task.Delay(durationMs);
        toast.Opacity = 0; // 淡出
        await Task.Delay(260);
        if (Dispatcher.UIThread.CheckAccess()) Toasts.Remove(toast);
        else Dispatcher.UIThread.Post(() => Toasts.Remove(toast));
    }
}
