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

    public static Task LoadAsync(string? url, Action<Bitmap?> onLoaded, CancellationToken ct = default)
        => LoadAsync(url, onLoaded, 96, ct);

    /// <summary>按目标宽度解码（大图降采样节省内存）</summary>
    public static async Task LoadAsync(string? url, Action<Bitmap?> onLoaded, int decodeWidth, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url))
        {
            onLoaded(null);
            return;
        }
        try
        {
            var task = Cache.GetOrAdd(url, static u => DownloadAsync(u, 96));
            var bitmap = decodeWidth <= 96
                ? await task
                : await DownloadAsync(url, decodeWidth); // 大图单独解码（不污染小图缓存）
            onLoaded(bitmap);
        }
        catch
        {
            // 失败也缓存 null：切 tab 反复重建视图时不再重复请求坏图（秒切换的关键）
            Cache[url] = Task.FromResult<Bitmap?>(null);
            onLoaded(null);
        }
    }

    private static async Task<Bitmap?> DownloadAsync(string url, int decodeWidth)
    {
        using var resp = await Http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync();
        return Bitmap.DecodeToWidth(stream, decodeWidth);
    }
}
