using Avalonia.Controls;
using Launcher.App.Views;

namespace Launcher.App.Services;

/// <summary>
/// 全局确认/提示对话框服务（模态窗口）。替换"点一次变确认"式 hack 与内联错误文本。
/// </summary>
public static class DialogService
{
    /// <summary>
    /// 确认对话框 → true=确认 / false=取消。cancel 传空字符串隐藏取消按钮（仅确定）。
    /// </summary>
    public static Task<bool> Confirm(Window? owner, string message,
        string title = "确认", string confirm = "确定", string cancel = "取消")
        => MessageDialogWindow.Confirm(owner, message, title, confirm, cancel);

    /// <summary>信息对话框（仅确定）</summary>
    public static Task<bool> Info(Window? owner, string message, string title = "提示")
        => MessageDialogWindow.Confirm(owner, message, title, "知道了", "");

    /// <summary>
    /// 警告对话框：红字加粗原因 + 普通色说明（前提不满足弹窗化——替代无着重色的状态栏小字）。
    /// 返回 true=确认（如"立即下载并启动"） / false=取消。
    /// </summary>
    public static Task<bool> Warn(Window? owner, string reason, string detail,
        string title = "无法继续", string confirm = "确定", string cancel = "取消")
        => MessageDialogWindow.Warn(owner, reason, detail, title, confirm, cancel);

    /// <summary>取当前主窗口（App.MainWindow）</summary>
    public static Window? MainWindow()
    {
        if (ApplicationLifetimeHolder.Desktop?.MainWindow is { } win) return win;
        return null;
    }
}

/// <summary>应用生命周期持有（避免 Services 直接依赖 App 类）</summary>
internal static class ApplicationLifetimeHolder
{
    public static Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime? Desktop =>
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime);
}
