using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Account;
using Launcher.Core.Download;
using Launcher.Core.Launch;
using Launcher.Core.Utils;

namespace Launcher.App.ViewModels;

/// <summary>
/// LittleSkin 皮肤库窗口（8-16 批次 51）：OAuth 设备码连接 → 角色选择 + 衣柜浏览 → 应用/下载皮肤。
/// token 持久化 LittleSkinTokenStore（DPAPI）；401 自愈（刷新重试一次，再失败清 token 回未连接）。
/// </summary>
public partial class SkinLibraryViewModel : ViewModelBase
{
    private const string OAuthManageUrl = "https://littleskin.cn/user/oauth/manage";
    private const string SkinlibWebUrl = "https://littleskin.cn/skinlib";

    private readonly LittleSkinApi _api;
    private readonly LittleSkinTokenStore _store;
    private readonly HttpClient _http;
    private CancellationTokenSource? _connectCts;
    private bool _tokenRefreshed; // 401 自愈只重试一次

    // —— 连接状态 ——
    [ObservableProperty]
    public partial bool IsConnected { get; set; }
    [ObservableProperty]
    public partial bool IsConnecting { get; set; }
    [ObservableProperty]
    public partial string DeviceCodeText { get; set; } = "";
    [ObservableProperty]
    public partial string? VerifyUrl { get; set; }
    [ObservableProperty]
    public partial string Status { get; set; } = "";
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    // —— 角色 + 衣柜 ——
    public ObservableCollection<PlayerInfo> Players { get; } = [];
    [ObservableProperty]
    public partial PlayerInfo? SelectedPlayer { get; set; }
    public ObservableCollection<ClosetItemVM> Closet { get; } = [];
    [ObservableProperty]
    public partial bool IsLoadingCloset { get; set; }
    [ObservableProperty]
    public partial bool IsError { get; set; }
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";
    [ObservableProperty]
    public partial int CurrentPage { get; set; } = 1;
    [ObservableProperty]
    public partial bool HasPrev { get; set; }
    [ObservableProperty]
    public partial bool HasNext { get; set; }
    [ObservableProperty]
    public partial string PageText { get; set; } = "";
    [ObservableProperty]
    public partial string? BusyItemTid { get; set; } // 单卡忙碌标记（应用/下载中）

    public SkinLibraryViewModel(LittleSkinTokenStore? store = null)
    {
        _store = store ?? LittleSkinTokenStore.Shared;
        _http = HttpClientPool.Create(TimeSpan.FromSeconds(20));
        _api = new LittleSkinApi(_http, () => _store.Load()?.AccessToken);
        RestoreSession();
    }

    private void RestoreSession()
    {
        if (_store.Load() is not null)
        {
            IsConnected = true;
            _ = LoadAllAsync();
        }
    }

    // ---------- 连接 ----------

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var clientId = LauncherSettings.Current.LittleSkinClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            Status = "还没填 LittleSkin Client ID：设置 → 外观 → LittleSkin Client ID";
            OpenUrl(OAuthManageUrl);
            return;
        }
        IsConnecting = true;
        IsBusy = true;
        Status = "正在发起连接…";
        _connectCts = new CancellationTokenSource();
        try
        {
            var session = await LittleSkinOAuth.StartDeviceCodeAsync(_http, clientId, _connectCts.Token);
            DeviceCodeText = session.UserCode;
            VerifyUrl = session.VerificationUriComplete;
            OpenUrl(string.IsNullOrEmpty(session.VerificationUriComplete) ? session.VerificationUri : session.VerificationUriComplete);
            Status = "请在浏览器中输入配对码并授权";
            var tokens = await LittleSkinOAuth.PollDeviceCodeAsync(_http, clientId, session, s => Status = s, _connectCts.Token);
            _store.Save(tokens);
            IsConnecting = false;
            IsConnected = true;
            Status = "连接成功";
            _ = LoadAllAsync();
            // 8-18 连接即登录：皮肤库连接成功 → 自动成为游戏账号（连贯性——两套登录打通）
            _ = Task.Run(async () =>
            {
                try
                {
                    var players = await _api.GetPlayersAsync(CancellationToken.None);
                    var name = players.FirstOrDefault()?.Name;
                    if (string.IsNullOrEmpty(name)) return;
                    var uuid = await _api.GetUuidByNameAsync(name, CancellationToken.None);
                    Launcher.Core.Account.AccountService.Shared.LoginLittleskin(name, uuid);
                    NotificationService.Success($"已切换为 LittleSkin 账号 {name}");
                }
                catch { /* 连接成功但账号同步失败不阻塞皮肤库使用 */ }
            });
        }
        catch (OperationCanceledException)
        {
            IsConnecting = false;
            Status = "已取消连接";
        }
        catch (TimeoutException)
        {
            IsConnecting = false;
            Status = "授权超时，请重新发起连接";
        }
        catch (Exception ex)
        {
            IsConnecting = false;
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelConnect()
    {
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = null;
    }

    [RelayCommand]
    private void ReopenVerifyPage()
    {
        if (!string.IsNullOrEmpty(VerifyUrl)) OpenUrl(VerifyUrl);
    }

    [RelayCommand]
    private void Disconnect()
    {
        CancelConnect();
        _store.Clear();
        IsConnected = false;
        IsConnecting = false;
        DeviceCodeText = "";
        VerifyUrl = null;
        Status = "";
        Players.Clear();
        Closet.Clear();
        SelectedPlayer = null;
        IsError = false;
    }

    [RelayCommand]
    private void OpenWebSkinlib() => OpenUrl(SkinlibWebUrl);

    // ---------- 衣柜 ----------

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (CurrentPage <= 1) return;
        CurrentPage--;
        await LoadClosetAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!HasNext) return;
        CurrentPage++;
        await LoadClosetAsync();
    }

    private async Task LoadAllAsync()
    {
        try
        {
            Players.Clear();
            foreach (var p in await WithRefreshAsync(ct => _api.GetPlayersAsync(ct))) Players.Add(p);
            if (Players.Count > 0) SelectedPlayer = Players[0]; // 默认第一个角色
            await LoadClosetAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task LoadClosetAsync()
    {
        IsLoadingCloset = true;
        IsError = false;
        try
        {
            var page = await WithRefreshAsync(ct => _api.GetClosetAsync("skin", CurrentPage, ct));
            Closet.Clear();
            foreach (var item in page.Items) Closet.Add(new ClosetItemVM(item));
            HasPrev = CurrentPage > 1;
            HasNext = page.HasMore;
            PageText = $"第 {CurrentPage} 页";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { IsLoadingCloset = false; }
    }

    // ---------- 应用 / 下载 ----------

    [RelayCommand]
    private async Task ApplySkinAsync(ClosetItemVM? item)
    {
        if (item is null) return;
        if (SelectedPlayer is null) { Status = "先选一个角色再应用皮肤"; return; }
        BusyItemTid = item.Item.Tid.ToString();
        try
        {
            await WithRefreshAsync(ct => _api.ApplySkinAsync(SelectedPlayer.Pid, item.Item.Tid, ct));
            var wrote = await DownloadToLocalAsync(SelectedPlayer.Name, item.Item);
            Status = $"已应用到 {SelectedPlayer.Name}" + (wrote ? "，游戏内与本地头像同步生效" : "（游戏内已生效，本地保存失败）");
            RefreshHomeAvatar();
            NotificationService.Success(Status);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { BusyItemTid = null; }
    }

    [RelayCommand]
    private async Task DownloadSkinAsync(ClosetItemVM? item)
    {
        if (item is null) return;
        if (SelectedPlayer is null) { Status = "先选一个角色（决定保存文件名）"; return; }
        BusyItemTid = item.Item.Tid.ToString();
        try
        {
            var wrote = await DownloadToLocalAsync(SelectedPlayer.Name, item.Item);
            if (wrote)
            {
                Status = "已保存到本地，下次启动游戏生效";
                RefreshHomeAvatar();
                NotificationService.Success(Status);
            }
            else Status = "本地保存失败，请重试";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { BusyItemTid = null; }
    }

    /// <summary>下载皮肤原图写本地（yggdrasil 纹理路径 404 → 降级 /textures/{hash}）；返回是否成功</summary>
    private async Task<bool> DownloadToLocalAsync(string playerName, LittleSkinApi.ClosetItem item)
    {
        byte[]? bytes = null;
        foreach (var url in new[] { LittleSkinApi.SkinFileUrl(playerName), $"https://littleskin.cn/textures/{item.Hash}" })
        {
            try
            {
                using var resp = await _http.GetAsync(url);
                if (resp.IsSuccessStatusCode) { bytes = await resp.Content.ReadAsByteArrayAsync(); break; }
            }
            catch { /* 换下一个候选 */ }
        }
        if (bytes is null) return false;

        var size = SkinPngHeader.TryParse(bytes);
        if (size is not { } dims || !SkinPack.IsSupportedSize(dims.Width, dims.Height))
            return false; // 尺寸不支持（或非 PNG）——不写本地（游戏内 PUT 已生效）

        var dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Launcher", "skins", $"{playerName}.png"); // 与 HomeViewModel.LocalSkinPath 同规则
        SkinFileWriter.ForceWrite(dest, bytes);
        return true;
    }

    /// <summary>刷新主页头像（应用/下载成功后本地头像即时更新）</summary>
    private static void RefreshHomeAvatar()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var home = MainViewModel.Current?.Home;
            home?.RefreshPlayer();
        });
    }

    // ---------- 401 自愈 + 通用 ----------

    /// <summary>执行 API 调用（Task）：401 → 刷新 token 重试一次（刷新失败清 token 回未连接）</summary>
    private async Task WithRefreshAsync(Func<CancellationToken, Task> work)
    {
        try
        {
            await work(CancellationToken.None);
        }
        catch (LittleSkinApi.UnauthorizedException)
        {
            await RefreshAndRetryAsync(() => work(CancellationToken.None));
        }
    }

    /// <summary>执行 API 调用（Task&lt;T&gt;）：同上。
    /// 8-22 全栈排查：旧代码 RefreshAndRetryAsync 内部已带新 token 重试一次，返回后外层又执行第三次
    /// （ApplySkin PUT 发两次、衣柜加载两遍）——改为直接返回自愈结果</summary>
    private async Task<T> WithRefreshAsync<T>(Func<CancellationToken, Task<T>> work)
    {
        try
        {
            return await work(CancellationToken.None);
        }
        catch (LittleSkinApi.UnauthorizedException)
        {
            return await RefreshAndRetryAsync(() => work(CancellationToken.None));
        }
    }

    /// <summary>401 自愈（有返回值版）：刷新 token 一次，带新 token 重试恰一次并返回结果；
    /// 仍 401 则断连。8-22 全栈排查：旧实现「内部重试一次 + 外层又执行一次」= 双重执行</summary>
    private async Task<T> RefreshAndRetryAsync<T>(Func<Task<T>> retry)
    {
        if (_tokenRefreshed)
        {
            Disconnect(); // 刷新后仍 401 → 授权真失效，清 token
            throw new InvalidOperationException("LittleSkin 授权已失效，请重新连接");
        }
        _tokenRefreshed = true;
        var clientId = LauncherSettings.Current.LittleSkinClientId;
        var current = _store.Load();
        if (string.IsNullOrWhiteSpace(clientId) || current is null)
            throw new InvalidOperationException("LittleSkin 未连接，请重新连接");
        var fresh = await LittleSkinOAuth.RefreshAsync(_http, clientId, current.RefreshToken, CancellationToken.None);
        _store.Save(fresh);
        try
        {
            return await retry(); // 带新 token 重试恰一次
        }
        catch (LittleSkinApi.UnauthorizedException)
        {
            Disconnect(); // 新 token 仍 401 → 授权真失效
            throw new InvalidOperationException("LittleSkin 授权已失效，请重新连接");
        }
        finally
        {
            _tokenRefreshed = false; // 8-22 复位：LittleSkin token 短效，下次过期可再自愈（旧代码永不复位 → 第二次过期直接登出）
        }
    }

    /// <summary>401 自愈（无返回值版）：同上，void 场景用</summary>
    private async Task RefreshAndRetryAsync(Func<Task> retry)
        => await RefreshAndRetryAsync(async () => { await retry(); return 0; });

    private void ShowError(Exception ex)
    {
        IsError = true;
        ErrorMessage = ex.Message;
        Status = ex.Message;
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* 打不开浏览器不致命 */ }
    }
}

/// <summary>衣柜卡片 VM：缩略图懒加载（ImageLoader 磁盘缓存 + 并发门）</summary>
public partial class ClosetItemVM : ObservableObject
{
    public LittleSkinApi.ClosetItem Item { get; }
    public string TypeText => Item.Type == "alex" ? "Alex" : "Steve";

    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; set; }

    public ClosetItemVM(LittleSkinApi.ClosetItem item)
    {
        Item = item;
        _ = ImageLoader.LoadAsync(LittleSkinApi.PreviewUrl(item.Tid), bmp => Thumbnail = bmp, 96);
    }
}
