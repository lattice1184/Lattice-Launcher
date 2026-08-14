using Avalonia.Controls;
using Launcher.App.Animations;
using Launcher.App.ViewModels;
using Launcher.Core.Multiplayer;

namespace Launcher.App.Views;

/// <summary>
/// 联机协议弹窗：模块已装则不弹；未装则弹出，同意 → 下载进度，不同意 → 关窗。
/// 返回 true=模块就绪，false=未同意（联机页顶部提示）。
/// </summary>
public partial class TerracottaAgreementDialog : Window
{
    public TerracottaAgreementDialog(TerracottaProvisioningService provisioning)
    {
        InitializeComponent();
        Opened += (_, _) => UiAnim.PopIn(Root); // 弹入动画（项目对话框风格）
        DataContext = new TerracottaAgreementDialogViewModel(
            provisioning,
            result =>
            {
                Close(result);
                return Task.CompletedTask;
            });
    }
}
