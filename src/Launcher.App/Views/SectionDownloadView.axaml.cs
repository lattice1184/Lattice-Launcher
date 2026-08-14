using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Launcher.App.Animations;
using Launcher.App.Services;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class SectionDownloadView : UserControl
{
    public SectionDownloadView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm) vm.PropertyChanged += OnVmPropertyChanged;
        };
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    /// <summary>8-13 Cloudflare 风验证动画：Valid=绿块弹簧弹出+对勾一笔描出；Invalid=红块弹出；其他=隐藏图标</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.CfStatus) || Vm is not { } vm) return;
        switch (vm.CfStatus)
        {
            case SettingsViewModel.CfStatusKind.Valid: PlayCheck(CfValidIcon, CfCheckPath); break;
            case SettingsViewModel.CfStatusKind.Invalid: PlayCheck(CfInvalidIcon, null); break;
            default:
                CfValidIcon.IsVisible = false;
                CfInvalidIcon.IsVisible = false;
                break;
        }
    }

    /// <summary>图标块弹簧弹出（scale 0.7 → 过冲 ~1.08 → 1.0）；带对勾时对勾一笔描出（DashOffset 推进）</summary>
    private void PlayCheck(Border icon, Avalonia.Controls.Shapes.Path? check)
    {
        CfValidIcon.IsVisible = ReferenceEquals(icon, CfValidIcon);
        CfInvalidIcon.IsVisible = ReferenceEquals(icon, CfInvalidIcon);
        var scale = new ScaleTransform(0.7, 0.7);
        icon.RenderTransform = scale;
        UiAnim.Animate(350, UiAnim.Curves.Overshoot, e =>
        {
            scale.ScaleX = scale.ScaleY = 0.7 + 0.3 * e;
        }, null, icon, slot: "cfpop");
        if (check is not null)
        {
            const double len = 27.0; // 对勾路径总长近似（描边推进用）
            check.StrokeDashArray = new AvaloniaList<double> { len };
            check.StrokeDashOffset = len;
            UiAnim.Animate(250, UiAnim.Curves.Decelerate, e =>
                check.StrokeDashOffset = len * (1 - e), null, check, slot: "cfcheck");
        }
    }

    /// <summary>验证 CurseForge API Key：先提交输入框内容（有输入才覆盖），再直连验证（结果只含状态，key 永不回显）</summary>
    private async void OnCheckProxy(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        await Vm.SubmitApiKeyAsync();
    }

    /// <summary>8-14 GitHub token 输入框失焦即保存（粘贴 token 后点别处就落盘——DPAPI 加密）</summary>
    private void OnGitHubTokenLostFocus(object? sender, RoutedEventArgs e)
    {
        Vm?.SaveGitHubApiToken();
    }

    /// <summary>打开 CurseForge API 控制台（PCL 式「获取 API」引导：Google 登录，申请表单必填网站地址 + Git 地址）</summary>
    private void OnOpenCfConsole(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://console.curseforge.com/") { UseShellExecute = true }); }
        catch { NotificationService.Error("无法打开浏览器，请手动访问 console.curseforge.com"); }
    }

    /// <summary>清理下载缓存：删 *.parts 残留，Toast 报告释放空间</summary>
    private void OnClearDownloadCache(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var (dirs, bytes) = Vm.ClearDownloadCache();
        if (dirs == 0)
        {
            NotificationService.Info("没有需要清理的下载缓存");
            return;
        }
        var size = bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{bytes / 1024.0:0} KB";
        NotificationService.Success($"已清理 {dirs} 个临时目录，释放 {size}");
    
}

}
