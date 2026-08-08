using Avalonia.Controls;
using Avalonia.Interactivity;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

/// <summary>创建房间结果（取消时对话框返回 null）</summary>
public sealed record CreateRoomResult(string VersionId, string RoomName, int Port);

public partial class CreateRoomWindow : Window
{
    public CreateRoomWindow(IEnumerable<VersionInstanceVM> versions)
    {
        InitializeComponent();
        VersionBox.ItemsSource = versions;
        if (versions.FirstOrDefault() is { } first) VersionBox.SelectedItem = first;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (VersionBox.SelectedItem is not VersionInstanceVM version)
        {
            ShowError("请选择游戏版本");
            return;
        }
        if (!int.TryParse(PortBox.Text?.Trim(), out var port) || port is < 1 or > 65535)
        {
            ShowError("端口需为 1~65535 的数字");
            return;
        }
        Close(new CreateRoomResult(version.Name, NameBox.Text?.Trim() ?? "", port));
    }

    private void ShowError(string message)
    {
        HintText.Text = message;
        HintText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E05A5A"));
    }
}
