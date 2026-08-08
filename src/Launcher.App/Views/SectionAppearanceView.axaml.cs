using Avalonia.Controls;
using Avalonia.Interactivity;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class SectionAppearanceView : UserControl
{
    public SectionAppearanceView() => InitializeComponent();

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private void OnDensityClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string idx } && int.TryParse(idx, out var i))
            Vm!.DensityIndex = i;
    }
}
