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

    /// <summary>淡出透明度（视图 DoubleTransition 绑定）</summary>
    [ObservableProperty]
    public partial double Opacity { get; set; } = 1;

    /// <summary>滑入/滑出偏移（进入 24→0，退出 0→-24；视图 TranslateTransform Y 绑定）</summary>
    [ObservableProperty]
    public partial double SlideY { get; set; } = 24;

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

    /// <summary>弹一条 Toast（滑入 + 停留 + 滑出，自动移除）</summary>
    public static void Show(string message, ToastType type = ToastType.Info, int durationMs = 3200)
    {
        var toast = new ToastItem(message, type);
        if (Dispatcher.UIThread.CheckAccess()) Toasts.Add(toast);
        else Dispatcher.UIThread.Post(() => Toasts.Add(toast));
        // 滑入：24 → 0（视图 DoubleTransition 平滑）
        Dispatcher.UIThread.Post(() => toast.SlideY = 0);
        _ = FadeOutAsync(toast, durationMs);
    }

    /// <summary>成功提示快捷方式</summary>
    public static void Success(string message, int durationMs = 3200) => Show(message, ToastType.Success, durationMs);

    /// <summary>错误提示快捷方式</summary>
    public static void Error(string message, int durationMs = 4500) => Show(message, ToastType.Error, durationMs);

    private static async Task FadeOutAsync(ToastItem toast, int durationMs)
    {
        await Task.Delay(durationMs);
        // 滑出：0 → -24 + 淡出
        toast.SlideY = -24;
        toast.Opacity = 0;
        await Task.Delay(260);
        if (Dispatcher.UIThread.CheckAccess()) Toasts.Remove(toast);
        else Dispatcher.UIThread.Post(() => Toasts.Remove(toast));
    }
}
