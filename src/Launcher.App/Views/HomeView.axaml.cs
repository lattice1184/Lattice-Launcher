using System.Collections.Specialized;
using Avalonia.Controls;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is HomeViewModel vm)
                vm.GameLogs.CollectionChanged += OnLogsChanged;
        };
    }

    /// <summary>新日志到达时控制台自动滚动到底部</summary>
    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => LogScroll?.ScrollToEnd());
    }
}
