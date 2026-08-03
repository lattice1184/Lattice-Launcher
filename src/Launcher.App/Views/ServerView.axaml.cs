using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class ServerView : UserControl
{
    public ServerView()
    {
        InitializeComponent();
    }

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Send();
    }

    private void OnSendClick(object? sender, RoutedEventArgs e) => Send();

    private void Send()
    {
        if (DataContext is not ServerViewModel vm) return;
        var box = this.FindControl<TextBox>("CommandBox");
        if (box is null) return;
        var cmd = box.Text;
        vm.SendCommandCommand.Execute(cmd);
        box.Text = "";
    }
}
