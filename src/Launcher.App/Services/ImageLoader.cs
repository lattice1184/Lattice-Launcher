using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace Launcher.App.Services;

/// <summary>
/// 图片异步加载器：内存缓存 + 并发去重 + 降采样 + 磁盘缓存（AL65——切 tab/翻页不重下图标）。
/// 磁盘缓存 %LocalAppData%\Launcher\imgcache\{sha256(url)}；下载并发门 4（不抢主下载连接）；
/// 8s 超时（图标小，15s 太宽）。
/// </summary>
public static class ImageLoader
{
    private static readonly HttpClient Http = Launcher.Core.Download.HttpClientPool.Create(TimeSpan.FromSeconds(8));
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new();
    private static readonly SemaphoreSlim Gate = new(4);
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Launcher", "imgcache");
    /// <summary>内存位图缓存上限（8-22 内存瘦身：旧实现只增不减——翻页/切 tab 攒几十上百张
    /// 位图常驻（每张 96px 解码 ≈36KB，300 张 = 10MB+）。超限整体清空：磁盘缓存（imgcache）
    /// 兜底重新解码（毫秒级），无泄漏无失效。按「近似 LRU」——字典无序，整体清最简。</summary>
    private const int CacheMaxEntries = 128;

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
            TrimIfNeeded(); // 8-22 超限整体清空（磁盘缓存兜底）
            var task = Cache.GetOrAdd(url, static u => DownloadAsync(u, 96));
            var bitmap = decodeWidth <= 96
                ? await task
                : await DownloadAsync(url, decodeWidth); // 大图单独解码（不污染小图缓存）
            // 8-22 回调封送 UI 线程：解码/下载在后台，直接回调 = 线程池触发绑定更新——
            // 搜索后 20 张图标同时完成 → Avalonia UI 线程排队风暴（「搜索时明显变卡」主因）
            Avalonia.Threading.Dispatcher.UIThread.Post(() => onLoaded(bitmap));
        }
        catch
        {
            // 失败也缓存 null：切 tab 反复重建视图时不再重复请求坏图（秒切换的关键）
            Cache[url] = Task.FromResult<Bitmap?>(null);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => onLoaded(null));
        }
    }

    /// <summary>容量裁剪：超过上限整体清空（近似 LRU 的最简实现——位图重新解码有磁盘缓存兜底）</summary>
    private static void TrimIfNeeded()
    {
        if (Cache.Count <= CacheMaxEntries) return;
        foreach (var key in Cache.Keys)
            Cache.TryRemove(key, out _);
    }

    private static async Task<Bitmap?> DownloadAsync(string url, int decodeWidth)
    {
        var path = CachePath(url);
        // 磁盘缓存命中：本地直接解码（无网络）
        if (File.Exists(path))
        {
            await using var fs = File.OpenRead(path);
            return Bitmap.DecodeToWidth(fs, decodeWidth);
        }
        // 下载并发门：最多 4 个图片请求同时进行
        await Gate.WaitAsync();
        try
        {
            // 双重检查（并发下其他线程可能已写入）
            if (File.Exists(path))
            {
                await using var fs = File.OpenRead(path);
                return Bitmap.DecodeToWidth(fs, decodeWidth);
            }
            using var resp = await Http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            try { Directory.CreateDirectory(CacheDir); await File.WriteAllBytesAsync(path, bytes); } catch { }
            using var ms = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(ms, decodeWidth);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string CachePath(string url)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(CacheDir, key);
    }
}
