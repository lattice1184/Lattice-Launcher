using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Launcher.App.Views;

public partial class SkinLibraryWindow : Window
{
    public SkinLibraryWindow()
    {
        InitializeComponent();
        // 关闭即取消设备码轮询——防窗口关了后台还在空轮询
        Closing += (_, _) =>
        {
            if (DataContext is ViewModels.SkinLibraryViewModel vm)
                vm.CancelConnectCommand.Execute(null);
        };
    }

    private void OnCopyDeviceCode(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SkinLibraryViewModel vm && !string.IsNullOrEmpty(vm.DeviceCodeText))
            Clipboard.SetTextAsync(vm.DeviceCodeText);
    }
}
