using Avalonia.Controls;
using Avalonia.Interactivity;

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
    }

    /// <summary>展示确认框并等待用户决定（cancel 传 "" 隐藏取消按钮）</summary>
    public static async Task<bool> Confirm(Window? owner, string message,
        string title, string confirm, string cancel)
    {
        var win = new MessageDialogWindow
        {
            Title = title,
        };
        win.MessageText.Text = message;
        win.ConfirmBtn.Content = confirm;
        win.CancelBtn.Content = cancel;
        win.CancelBtn.IsVisible = cancel.Length > 0;

        var tcs = new TaskCompletionSource<bool>();
        win._result = tcs;
        if (owner is not null) await win.ShowDialog(owner);
        else win.Show();
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
}
