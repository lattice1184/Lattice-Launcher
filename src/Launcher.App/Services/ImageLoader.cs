using System.Collections.Concurrent;
using System.Net.Http;
using Avalonia.Media.Imaging;

namespace Launcher.App.Services;

/// <summary>
/// 图片异步加载器：内存缓存 + 并发去重 + 降采样（生态卡片图标用）。
/// </summary>
public static class ImageLoader
{
    private static readonly HttpClient Http = new();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new();

    public static async Task LoadAsync(string? url, Action<Bitmap?> onLoaded, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url))
        {
            onLoaded(null);
            return;
        }
        try
        {
            var task = Cache.GetOrAdd(url, static u => DownloadAsync(u));
            onLoaded(await task);
        }
        catch
        {
            // 失败任务不永久缓存：移除后下次重新下载（网络恢复后可自愈）
            Cache.TryRemove(url, out _);
            onLoaded(null);
        }
    }

    private static async Task<Bitmap?> DownloadAsync(string url)
    {
        using var resp = await Http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync();
        return Bitmap.DecodeToWidth(stream, 96);
    }
}
