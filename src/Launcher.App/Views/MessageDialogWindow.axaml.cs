using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Launcher.App.Views;

/// <summary>
/// 模态确认对话框（DialogService.Confirm 载体）：消息 + 确认/取消按钮。
/// </summary>
public partial class MessageDialogWindow : Window
{
    private TaskCompletionSource<bool>? _result;

    public MessageDialogWindow()
    {
        InitializeComponent();
        global::Launcher.App.Animations.UiAnim.AttachDialog(this, Root);
    }

    /// <summary>展示确认框并等待用户决定（cancel 传 "" 隐藏取消按钮）</summary>
    public static async Task<bool> Confirm(Window? owner, string message,
        string title, string confirm, string cancel)
    {
        var win = new MessageDialogWindow { Title = title };
        win.MessageText.Text = message;
        win.ConfirmBtn.Content = confirm;
        win.CancelBtn.Content = cancel;
        win.CancelBtn.IsVisible = cancel.Length > 0;
        return await ShowAndWaitAsync(owner, win);
    }

    /// <summary>
    /// 警告对话框：红字加粗原因 + 普通色说明（前提不满足/操作失败弹窗化，替代无着重色的状态栏小字）。
    /// </summary>
    public static async Task<bool> Warn(Window? owner, string reason, string detail,
        string title, string confirm = "确定", string cancel = "取消")
    {
        var win = new MessageDialogWindow { Title = title };
        win.MessageText.Text = reason;
        win.MessageText.Foreground = new SolidColorBrush(Color.Parse("#E05A5A")); // Danger 红
        win.MessageText.FontWeight = FontWeight.SemiBold;                          // 加粗表示原因
        win.DetailText.Text = detail;
        win.DetailText.IsVisible = detail.Length > 0;
        win.ConfirmBtn.Content = confirm;
        win.CancelBtn.Content = cancel;
        win.CancelBtn.IsVisible = cancel.Length > 0;
        return await ShowAndWaitAsync(owner, win);
    }

    private static async Task<bool> ShowAndWaitAsync(Window? owner, MessageDialogWindow win)
    {
        var tcs = new TaskCompletionSource<bool>();
        win._result = tcs;
        try
        {
            // owner 不可见/未加载时 ShowDialog 抛异常（静默失败导致确认框不出现）——兜底独立窗口
            if (owner is { PlatformImpl: not null, IsVisible: true }) await win.ShowDialog(owner);
            else { win.WindowStartupLocation = WindowStartupLocation.CenterScreen; win.Show(); }
        }
        catch
        {
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            win.Show();
        }
        return await tcs.Task;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        _result?.TrySetResult(true);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result?.TrySetResult(false);
        Close();
    }

    /// <summary>兜底：标题栏 X / Alt+F4 / ESC 关闭也完成 Task（防调用方永久挂起）</summary>
    protected override void OnClosed(EventArgs e)
    {
        _result?.TrySetResult(false);
        base.OnClosed(e);
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Escape)
        {
            _result?.TrySetResult(false);
            Close();
            return;
        }
        base.OnKeyDown(e);
    }
}
