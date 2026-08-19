using Avalonia.Controls;

namespace Launcher.App.Views;

public partial class ServerWindow : Window
{
    public ServerWindow()
    {
        InitializeComponent();
        global::Launcher.App.Animations.UiAnim.AttachDialog(this, Root);
    }
}
