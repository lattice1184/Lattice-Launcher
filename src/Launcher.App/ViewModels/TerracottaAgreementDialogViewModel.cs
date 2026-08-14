using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Multiplayer;

namespace Launcher.App.ViewModels;

/// <summary>
/// 联机协议弹窗：未安装陶瓦模块时弹出，用户同意后开始下载并回显进度。
/// 不同意则关窗返回 false，联机页顶部提示并可随时重开。
/// </summary>
public partial class TerracottaAgreementDialogViewModel : ObservableObject
{
    private readonly TerracottaProvisioningService _provisioning;
    private readonly Func<bool, Task> _finish; // 通知窗口关闭（true=模块就绪 / false=不同意）

    /// <summary>状态文案（准备中 / 下载中 / 解压中 / 失败）</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "准备下载…";

    /// <summary>下载进度 0~100；null = 不确定（解压中）</summary>
    [ObservableProperty]
    public partial double? ProgressPercent { get; set; }

    /// <summary>百分比文案（仅下载阶段显示，如「43%」；解压/就绪时 null）</summary>
    [ObservableProperty]
    public partial string? PercentText { get; set; }

    /// <summary>下载中（禁用按钮）</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>下载失败（显示重试 / 暂不启用）</summary>
    [ObservableProperty]
    public partial bool IsFailed { get; set; }

    /// <summary>进度不确定（解压中）</summary>
    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }

    /// <summary>同意前初始选择区（下载中/失败后隐藏）</summary>
    [ObservableProperty]
    public partial bool IsInitialChoice { get; set; } = true;

    partial void OnIsBusyChanged(bool value) => IsInitialChoice = !value && !IsFailed;

    partial void OnIsFailedChanged(bool value) => IsInitialChoice = !IsBusy && !value;

    public TerracottaAgreementDialogViewModel(
        TerracottaProvisioningService provisioning, Func<bool, Task> finish)
    {
        _provisioning = provisioning;
        _finish = finish;
    }

    /// <summary>不同意：关窗，联机页显示提示条</summary>
    [RelayCommand]
    private void Decline() => _ = _finish(false);

    /// <summary>同意：开始下载安装</summary>
    [RelayCommand]
    private Task Agree() => DownloadAsync();

    /// <summary>下载失败后重试</summary>
    [RelayCommand]
    private Task Retry() => DownloadAsync();

    /// <summary>打开陶瓦项目主页（浏览器）</summary>
    [RelayCommand]
    private void OpenProject()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://github.com/burningtnt/Terracotta") { UseShellExecute = true });
        }
        catch { /* 打不开就算了 */ }
    }

    private async Task DownloadAsync()
    {
        MultiplayerLog.Log("协议窗: 用户同意,开始下载安装");
        IsBusy = true;
        IsFailed = false;
        IsIndeterminate = false;
        ProgressPercent = 0;
        PercentText = null;
        StatusText = "正在下载联机模块…";

        var progress = new Progress<TerracottaProvisionProgress>(p =>
        {
            switch (p.Stage)
            {
                case "terracotta-download":
                    IsIndeterminate = false;
                    ProgressPercent = p.Percent;
                    PercentText = $"{p.Percent:0}%";
                    StatusText = "正在下载联机模块…";
                    break;
                case "terracotta-extract":
                    IsIndeterminate = true;
                    PercentText = null;
                    StatusText = "正在解压安装…";
                    break;
                case "terracotta-ready":
                    IsIndeterminate = false;
                    PercentText = null;
                    StatusText = "联机模块已就绪。";
                    break;
            }
        });

        try
        {
            await _provisioning.EnsureAvailableAsync(progress);
            await _finish(true);
        }
        catch (Exception ex)
        {
            MultiplayerLog.Log($"协议窗: 下载失败 {ex.Message}");
            IsFailed = true;
            IsIndeterminate = false;
            ProgressPercent = null;
            PercentText = null;
            StatusText = $"下载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
