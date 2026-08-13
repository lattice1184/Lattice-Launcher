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
        if (string.IsNullOrEmpty(name)) { Status = "你还没填用户名"; return; }
        _accounts.LoginOffline(name);
        Status = $"已登录 {name}";
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
                $"删除账号「{row.Name}」？删了就找不回来了。", "删除账号", "删除", "取消"))
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

    /// <summary>微软登录进度（等待浏览器授权 / 认证中）</summary>
    [ObservableProperty]
    public partial string MsAuthStatus { get; set; } = "";

    /// <summary>8-13 设备码登录：配对码（微软服务器生成，8 位）大字显示，用户在浏览器里输入</summary>
    [ObservableProperty]
    public partial string DeviceCodeText { get; set; } = "";

    /// <summary>浏览器输码页地址（重新打开网页按钮用；默认 microsoft.com/link）</summary>
    [ObservableProperty]
    public partial string DeviceCodeVerifyUri { get; set; } = "";

    /// <summary>是否处于设备码等待状态（显示配对码 + 重开网页/取消按钮）</summary>
    [ObservableProperty]
    public partial bool IsDeviceCodeMode { get; set; }

    /// <summary>取消设备码登录（用户在浏览器里输码期间可随时取消）</summary>
    private CancellationTokenSource? _msCts;

    /// <summary>微软正版登录（8-13 Live 设备码流）：配对码 → 浏览器输码登录 → 轮询拿 token → 认证链</summary>
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

            // 0. 解析 clientId（远程下发/缓存/兜底三层）——登录前保证生效值就绪
            await ClientIdRemote.ResolveAsync(http, CancellationToken.None);

            // 1. 发起设备码会话 → 显示配对码 + 打开浏览器输码页
            var session = await MicrosoftAuth.StartDeviceCodeAsync(http, CancellationToken.None);
            DeviceCodeText = session.UserCode;
            DeviceCodeVerifyUri = session.VerificationUri.Length > 0 ? session.VerificationUri : "https://www.microsoft.com/link";
            IsDeviceCodeMode = true;
            MsAuthStatus = "在打开的网页里输入配对码并登录";
            try { Process.Start(new ProcessStartInfo(DeviceCodeVerifyUri) { UseShellExecute = true }); }
            catch { /* 无法自动打开则手动访问 microsoft.com/link */ }

            // 2. 轮询等授权（可取消）→ 认证链
            _msCts = new CancellationTokenSource();
            var (oauthToken, refreshToken) = await MicrosoftAuth.PollDeviceCodeAsync(
                http, session, status => MsAuthStatus = status, _msCts.Token);
            MsAuthStatus = "正在认证 Minecraft…";
            var msSession = await MicrosoftAuth.AuthenticateMinecraftAsync(http, oauthToken, refreshToken, _msCts.Token);
            _accounts.LoginMicrosoft(msSession);
            MsAuthStatus = "";
            IsDeviceCodeMode = false;
            DeviceCodeText = "";
            Status = $"已以正版账号 {msSession.MinecraftName} 登录";
            NotificationService.Success($"正版账号 {msSession.MinecraftName} 登录成功");
            Refresh();
        }
        catch (OperationCanceledException)
        {
            MsAuthStatus = "";
            IsDeviceCodeMode = false;
            DeviceCodeText = "";
            Status = _msCts?.IsCancellationRequested == true
                ? "已取消登录"
                : "登录超时，请重新发起";
        }
        catch (Exception ex)
        {
            MsAuthStatus = "";
            IsDeviceCodeMode = false;
            DeviceCodeText = "";
            Status = $"登录失败: {ex.Message}";
            NotificationService.Error($"微软登录失败: {ex.Message}");
            LogMsError(ex.ToString());
        }
        finally
        {
            _msCts?.Dispose();
            _msCts = null;
            IsMsAuthBusy = false;
        }
    }

    /// <summary>8-13 重新打开输码网页（浏览器被关掉后不用重新发起登录）</summary>
    [RelayCommand]
    private void ReopenLoginPage()
    {
        if (DeviceCodeVerifyUri.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(DeviceCodeVerifyUri) { UseShellExecute = true }); }
        catch { NotificationService.Error("无法打开浏览器，请手动访问 microsoft.com/link"); }
    }

    /// <summary>8-13 取消设备码登录（停止轮询，收起等待区）</summary>
    [RelayCommand]
    private void CancelMsLogin() => _msCts?.Cancel();

    /// <summary>微软登录错误落盘（AppData\Launcher\logs\microsoft-auth.log）——下次失败可回看原因</summary>
    private static void LogMsError(string detail)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "microsoft-auth.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {detail}{Environment.NewLine}");
        }
        catch { }
    }
}
