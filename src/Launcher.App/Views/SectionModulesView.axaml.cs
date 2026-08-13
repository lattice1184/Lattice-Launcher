using Avalonia.Controls;
using Launcher.App.ViewModels;

using Launcher.App.Services;
namespace Launcher.App.Views;

public partial class SectionModulesView : UserControl
{
    public SectionModulesView()
    {
        InitializeComponent();
        // 进分区即扫一次存储（后台 IO，不抢启动）；之后手动「重新扫描」
        Loaded += (_, _) =>
        {
            if (DataContext is SettingsViewModel { Storage: { } storage })
                _ = storage.ReloadStatsCommand.ExecuteAsync(null);
        };
    
}

}
