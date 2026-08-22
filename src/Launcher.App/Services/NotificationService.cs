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

    /// <summary>8-19 第二批：相同文案合并计数（视图显示「文案 ×N」）；1 时显示原文</summary>
    [ObservableProperty]
    public partial int Count { get; set; } = 1;

    public string DisplayMessage => Count > 1 ? $"{Message} ×{Count}" : Message;

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(DisplayMessage));

    /// <summary>生效中的淡出计时（折叠续期时取消重启）；null = 未开始淡出</summary>
    public CancellationTokenSource? FadeCts { get; set; }

    /// <summary>已进入淡出阶段（不可再合并）</summary>
    public bool Removing { get; set; }

    public IBrush Dot => Type switch
    {
        ToastType.Success => new SolidColorBrush(Color.Parse("#5AD07C")),
        ToastType.Error => new SolidColorBrush(Color.Parse("#E05A5A")),
        _ => new SolidColorBrush(Color.Parse("#6C8CFF")),
    };

    public IBrush Border => Type switch
    {
        ToastType.Success => new SolidColorBrush(Color.Parse("#335AD07C")),
        ToastType.Error => new SolidColorBrush(Color.Parse("#33E05A5A")),
        _ => new SolidColorBrush(Color.Parse("#336C8CFF")),
    };

    /// <summary>文字色（AL7 红字规范：Error Toast 红字，其余主色）</summary>
    public IBrush MessageBrush => Type == ToastType.Error
        ? new SolidColorBrush(Color.Parse("#E05A5A"))
        : new SolidColorBrush(Color.Parse("#E8EAF0"));

    /// <summary>透明度（视图 DoubleTransition 绑定；初始 0 淡入）</summary>
    [ObservableProperty]
    public partial double Opacity { get; set; } = 0;

    /// <summary>移除前的滑出回调（视图层注册：MainWindow ContainerPrepared 挂 SlideOutToRight；动画时长被 Delay(260) 覆盖）</summary>
    public Action? OnRemoving { get; set; }

    /// <summary>8-22 步骤8：可选动作按钮（如「查看日志」）；null = 不显示</summary>
    public string? ActionText { get; init; }
    public Action? OnAction { get; init; }

    public bool HasAction => ActionText is not null && OnAction is not null;

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

    /// <summary>弹一条 Toast（淡入 + 停留 + 淡出，自动移除；全流程封送 UI 线程）。
    /// 8-19 第二批：相同 (文案, 类型) 的未过期条目折叠为 ×N 并重置停留计时——不再堆一串</summary>
    public static void Show(string message, ToastType type = ToastType.Info, int durationMs = 3200,
        string? actionText = null, Action? onAction = null)
    {
        var toast = new ToastItem(message, type)
        {
            ActionText = actionText,
            OnAction = onAction,
        };
        Dispatcher.UIThread.Post(() =>
        {
            // 折叠：同文案同类型且尚未淡出的条目合并（如多条「仍在搜索（网络较慢）」）
            foreach (var existing in Toasts)
            {
                if (existing.Type == type && existing.Message == message && !existing.Removing)
                {
                    existing.Count++;
                    existing.FadeCts?.Cancel(); // 旧计时作废
                    StartFade(existing, durationMs); // 重新计停留
                    return;
                }
            }
            Toasts.Add(toast);
            toast.Opacity = 1; // 淡入（容器 realize 后触发 0→1 过渡）
            StartFade(toast, durationMs);
        });
    }

    private static void StartFade(ToastItem toast, int durationMs)
    {
        toast.FadeCts = new CancellationTokenSource();
        _ = FadeOutAsync(toast, durationMs, toast.FadeCts.Token);
    }

    /// <summary>信息提示快捷方式</summary>
    public static void Info(string message, int durationMs = 3600,
        string? actionText = null, Action? onAction = null)
        => Show(message, ToastType.Info, durationMs, actionText, onAction);

    /// <summary>成功提示快捷方式</summary>
    public static void Success(string message, int durationMs = 3200,
        string? actionText = null, Action? onAction = null)
        => Show(message, ToastType.Success, durationMs, actionText, onAction);

    /// <summary>错误提示快捷方式</summary>
    public static void Error(string message, int durationMs = 4500,
        string? actionText = null, Action? onAction = null)
        => Show(message, ToastType.Error, durationMs, actionText, onAction);

    private static async Task FadeOutAsync(ToastItem toast, int durationMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(durationMs, ct);
        }
        catch (OperationCanceledException)
        {
            return; // 折叠续期替换了本计时，原任务静默退出
        }
        toast.Removing = true;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            toast.OnRemoving?.Invoke(); // 视图层滑出（180ms，与下方 Delay(260) 同步收尾）
            toast.Opacity = 0; // 淡出（UI 线程触发过渡）
        });
        await Task.Delay(260);
        await Dispatcher.UIThread.InvokeAsync(() => Toasts.Remove(toast));
    }
}
