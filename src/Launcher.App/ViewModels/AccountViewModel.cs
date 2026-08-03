using System.Collections.ObjectModel;
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

    // 微软正版登录（F4b）：OAuth 设备码 + Xbox/XSTS/Minecraft 认证链，待 ClientId 配置后接入
    [RelayCommand]
    private void LoginMicrosoft() => Status = "微软正版登录即将支持（需配置 ClientId）";
}
