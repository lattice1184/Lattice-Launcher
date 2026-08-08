using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Download;
using Launcher.Core.Utils;

namespace Launcher.App.ViewModels;

/// <summary>下载页「第三方文件」tab：粘贴直链 → 自动识别文件名 → 自定义目录 → 入全局队列（断点续传/历史自动接管）。</summary>
public partial class ThirdPartyDownloadViewModel : ViewModelBase
{
    /// <summary>文件名识别用 HEAD 请求（15 秒超时，识别失败回退 URL 段）</summary>
    private static readonly HttpClient NameHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>通用下载器（第三方 URL 不经镜像映射，按原 URL 下载）</summary>
    private static readonly DownloadService Downloader = new();

    /// <summary>防抖识别：取消上一轮未完成的识别</summary>
    private CancellationTokenSource? _recognizeCts;

    [ObservableProperty]
    public partial string UrlText { get; set; } = "";

    [ObservableProperty]
    public partial string FileNameText { get; set; } = "";

    [ObservableProperty]
    public partial string TargetDirText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>识别中（View 显示"正在识别文件名…"）</summary>
    [ObservableProperty]
    public partial bool IsRecognizing { get; set; }

    /// <summary>文件名就绪（自动识别到或手动填写），开始按钮可用</summary>
    [ObservableProperty]
    public partial bool CanStart { get; set; }

    public ThirdPartyDownloadViewModel() => TargetDirText = EffectiveDir();

    /// <summary>URL 输入变化 → 防抖 600ms 后自动识别文件名（识别到 → 文件名框填充 + 按钮亮起）</summary>
    partial void OnUrlTextChanged(string value)
    {
        _recognizeCts?.Cancel();
        _recognizeCts?.Dispose();
        _recognizeCts = new CancellationTokenSource();
        var ct = _recognizeCts.Token;
        var url = value.Trim();
        if (url.Length == 0)
        {
            IsRecognizing = false;
            FileNameText = "";
            CanStart = false;
            StatusText = "";
            return;
        }
        IsRecognizing = true;
        CanStart = false;
        StatusText = "";
        _ = RecognizeAsync(url, ct);
    }

    /// <summary>手动填写文件名 → 直接可用</summary>
    partial void OnFileNameTextChanged(string value) => CanStart = value.Trim().Length > 0;

    private async Task RecognizeAsync(string url, CancellationToken ct)
    {
        try
        {
            await Task.Delay(600, ct);
            var name = UriFileNameResolver.FromUrl(url);
            if (string.IsNullOrEmpty(name))
                name = await UriFileNameResolver.TryFromContentDispositionAsync(NameHttp, url, ct);
            if (ct.IsCancellationRequested) return;
            IsRecognizing = false;
            if (string.IsNullOrEmpty(name))
            {
                StatusText = "识别不到文件名，请手动填写";
                return;
            }
            FileNameText = name;
            CanStart = true;
        }
        catch (OperationCanceledException)
        {
            // 新一轮输入接管
        }
        catch
        {
            if (!ct.IsCancellationRequested)
            {
                IsRecognizing = false;
                StatusText = "识别不到文件名，请手动填写";
            }
        }
    }

    /// <summary>生效目录：设置记忆（已存在）→ 系统 Downloads</summary>
    private static string EffectiveDir()
    {
        var saved = LauncherSettings.Current.ThirdPartyDownloadDir;
        if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved)) return saved;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>FolderPicker 选择结果（视图 code-behind 调用）</summary>
    public void ApplyDir(string dir)
    {
        TargetDirText = dir;
        LauncherSettings.Current.ThirdPartyDownloadDir = dir;
        LauncherSettings.Current.Save();
        StatusText = "";
    }

    [RelayCommand]
    private async Task StartDownload()
    {
        if (IsBusy || !CanStart) return;
        var url = UrlText.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            NotificationService.Error("链接无效：请粘贴 http/https 直链");
            return;
        }
        var dir = TargetDirText.Trim();
        if (dir.Length == 0 || !Directory.Exists(dir))
        {
            NotificationService.Error("先选择下载目录");
            return;
        }
        var name = FileNameText.Trim();
        if (name.Length == 0)
        {
            NotificationService.Error("文件名未识别，请在文件名框手动填写");
            return;
        }
        name = UriFileNameResolver.Sanitize(name) ?? name;
        var dest = UniquePath.Resolve(Path.Combine(dir, name));
        IsBusy = true;
        try
        {
            // 传 sourceUrl/targetPath：下载历史「重新下载 / 打开位置」用
            DownloadManager.Instance.Enqueue($"下载 {name}", (p, ct) =>
                Downloader.DownloadFileAsync(url, dest, null, null, p, ct), url, dest);
            ApplyDir(dir); // 顺带记忆目录（下回直接是它）
            StatusText = $"已入队：{dest}";
            // 跳转①：入队即去下载记录看进度；完成后跳回本 tab（跳转②由下载中心统一处理）
            MainViewModel.Current?.NavigateToDownloadQueue("download:thirdparty");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
