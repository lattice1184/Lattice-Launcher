using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Account;

namespace Launcher.App.ViewModels;

/// <summary>账号页：离线登录 / 显示当前账号 / 退出。微软正版登录入口预留。</summary>
public partial class AccountViewModel : ViewModelBase
{
    private readonly AccountService _accounts = new();

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
    }

    [RelayCommand]
    private void LoginOffline()
    {
        var name = NameInput.Trim();
        if (string.IsNullOrEmpty(name)) { Status = "请输入用户名"; return; }
        var acc = _accounts.LoginOffline(name);
        Status = $"已以离线账号 {acc.Name} 登录";
        Refresh();
    }

    [RelayCommand]
    private void Logout()
    {
        _accounts.Logout();
        Status = "已退出登录";
        Refresh();
    }

    // 微软正版登录（F4b）：OAuth 设备码 + Xbox/XSTS/Minecraft 认证链，待 ClientId 配置后接入
    [RelayCommand]
    private void LoginMicrosoft() => Status = "微软正版登录即将支持（需配置 ClientId）";
}
