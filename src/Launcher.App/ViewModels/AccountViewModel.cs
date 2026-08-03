using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Account;

namespace Launcher.App.ViewModels;

/// <summary>账号行（列表展示 + 切换/删除）</summary>
public sealed record AccountRowVM(string Name, string TypeText, bool IsCurrent);

/// <summary>
/// 账号页：离线登录 + 多账号列表（切换/删除）+ 当前账号卡片。微软正版登录入口预留。
/// </summary>
public partial class AccountViewModel : ViewModelBase
{
    private readonly AccountService _accounts = AccountService.Shared;

    [ObservableProperty]
    public partial string NameInput { get; set; } = "";

    [ObservableProperty]
    public partial string CurrentName { get; set; } = "未登录";

    [ObservableProperty]
    public partial string CurrentUuid { get; set; } = "";

    [ObservableProperty]
    public partial string AccountType { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoggedIn { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial Bitmap? Avatar { get; set; }

    /// <summary>已保存账号列表（当前账号标记）</summary>
    public ObservableCollection<AccountRowVM> Accounts { get; } = [];

    public AccountViewModel()
    {
        _accounts.Load();
        Refresh();
    }

    private void Refresh()
    {
        var acc = _accounts.Current;
        IsLoggedIn = acc is not null;
        CurrentName = acc?.Name ?? "未登录";
        CurrentUuid = acc?.Uuid ?? "";
        AccountType = acc?.Type == "microsoft" ? "正版账号" : acc?.Type == "offline" ? "离线账号" : "";
        if (acc is not null) NameInput = acc.Name;

        Accounts.Clear();
        foreach (var a in _accounts.Accounts)
            Accounts.Add(new AccountRowVM(a.Name,
                a.Type == "microsoft" ? "正版" : "离线",
                a.Name == acc?.Name));

        // 玩家头像（minotar 渲染服务；离线名返回默认 Steve 皮肤，与游戏内一致）
        Avatar = null;
        if (acc is not null)
            _ = ImageLoader.LoadAsync($"https://minotar.net/helm/{Uri.EscapeDataString(acc.Name)}/64.png",
                bmp => Avatar = bmp);
    }

    [RelayCommand]
    private void LoginOffline()
    {
        var name = NameInput.Trim();
        if (string.IsNullOrEmpty(name)) { Status = "请输入用户名"; return; }
        _accounts.LoginOffline(name);
        Status = $"已以离线账号 {name} 登录";
        Refresh();
    }

    /// <summary>切换账号（点击列表行）</summary>
    [RelayCommand]
    private void SwitchAccount(AccountRowVM row)
    {
        if (_accounts.SwitchTo(row.Name))
        {
            Status = $"已切换到 {row.Name}";
            Refresh();
        }
    }

    /// <summary>删除账号（DialogService 确认；当前账号被删则退出）</summary>
    [RelayCommand]
    private async Task DeleteAccount(AccountRowVM row)
    {
        var owner = DialogService.MainWindow();
        if (owner is null || !await DialogService.Confirm(owner,
                $"删除账号「{row.Name}」？此操作不可恢复。", "删除账号", "删除", "取消"))
        {
            return;
        }
        if (_accounts.Delete(row.Name))
        {
            Status = $"已删除 {row.Name}";
            Refresh();
        }
    }

    [RelayCommand]
    private async Task Logout()
    {
        if (IsLoggedIn && DialogService.MainWindow() is { } owner)
        {
            if (!await DialogService.Confirm(owner,
                    "退出当前账号？", "退出登录", "退出", "取消"))
            {
                return;
            }
        }
        _accounts.Logout();
        Status = "已退出登录";
        Refresh();
    }

    [ObservableProperty]
    public partial bool IsMsAuthBusy { get; set; }

    /// <summary>设备码登录进度（user_code / 等待授权 / 认证中）</summary>
    [ObservableProperty]
    public partial string MsAuthStatus { get; set; } = "";

    /// <summary>微软正版登录：设备码流程（PCL 同款，Mojang 公开 client_id）</summary>
    [RelayCommand]
    private async Task LoginMicrosoft()
    {
        if (IsMsAuthBusy) return;
        IsMsAuthBusy = true;
        Status = "";
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);

            // 1. 设备码
            var device = await MicrosoftAuth.RequestDeviceCodeAsync(http, CancellationToken.None);
            MsAuthStatus = $"请在浏览器打开 {device.VerificationUri} 并输入代码：{device.UserCode}";
            try { Process.Start(new ProcessStartInfo(device.VerificationUri) { UseShellExecute = true }); }
            catch { /* 无法自动打开则手动 */ }

            // 2. 轮询授权（用户输码后自动继续；15 分钟超时）
            var oauthToken = await MicrosoftAuth.PollOAuthTokenAsync(http, device, CancellationToken.None);
            MsAuthStatus = "授权成功，正在认证 Minecraft…";

            // 3. Xbox/XSTS/Minecraft 认证链 → 正版账号
            var session = await MicrosoftAuth.AuthenticateMinecraftAsync(http, oauthToken, "", CancellationToken.None);
            _accounts.LoginMicrosoft(session);
            MsAuthStatus = "";
            Status = $"已以正版账号 {session.MinecraftName} 登录";
            NotificationService.Success($"正版账号 {session.MinecraftName} 登录成功");
            Refresh();
        }
        catch (OperationCanceledException) { MsAuthStatus = "已取消授权"; }
        catch (TimeoutException ex) { MsAuthStatus = ex.Message; }
        catch (Exception ex)
        {
            MsAuthStatus = "";
            Status = $"登录失败: {ex.Message}";
            NotificationService.Error($"微软登录失败: {ex.Message}");
        }
        finally
        {
            IsMsAuthBusy = false;
        }
    }
}
