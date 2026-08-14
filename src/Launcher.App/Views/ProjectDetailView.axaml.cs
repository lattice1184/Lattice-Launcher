using Avalonia.Controls;
using Avalonia.Interactivity;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class ProjectDetailView : UserControl
{
    public ProjectDetailView()
    {
        InitializeComponent();
    }

    private void OnOpenPage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectDetailViewModel vm) vm.OpenProjectPage();
    }
}
