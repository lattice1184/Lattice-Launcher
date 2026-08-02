using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Core.Model.Mojang;
using Launcher.Core.Utils;

namespace Launcher.Core.Download;

/// <summary>
/// 下载服务：大文件多连接 Range 分片并发（默认 8 段）+ 小文件单连接断点续传。
/// SHA1 校验 + 幂等 + 416 防御 + 分片失败回退单连接。
/// </summary>
public sealed class DownloadService
{
    private const int ChunkCount = 8;
    private const long ChunkThreshold = 2 * 1024 * 1024; // 2MB 以上走分片

    private readonly HttpClient _http;
    private readonly IDlSourceMapper _sourceMapper;
    private readonly string _gameDirectory;

    public DownloadService(HttpClient? http = null, IDlSourceMapper? sourceMapper = null, string? gameDirectory = null)
    {
        _http = http ?? CreateClient();
        _sourceMapper = sourceMapper ?? new DefaultDlSourceMapper();
        _gameDirectory = gameDirectory ?? GameDirectory.Detect();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YanKa-Launcher/0.1");
        return client;
    }

    public async Task DownloadFileAsync(
        string url, string destPath, string? expectedSha1, long? expectedSize,
        DownloadProgressHandler? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        // 幂等：完整文件且校验通过（SHA1 或大小）→ 跳过
        if (File.Exists(destPath))
        {
            var len = new FileInfo(destPath).Length;
            if (expectedSha1 is not null && await Sha1MatchesAsync(destPath, expectedSha1, ct))
                return;
            if (expectedSha1 is null && expectedSize is { } s && len == s)
                return;
        }

        var totalSize = expectedSize ?? await GetContentLengthAsync(url, ct);

        if (totalSize >= ChunkThreshold)
            await DownloadChunkedAsync(url, destPath, totalSize, expectedSha1, progress, ct);
        else
            await DownloadSingleAsync(url, destPath, expectedSha1, totalSize, progress, ct);
    }

    /// <summary>单连接下载（断点续传 + 416 防御 + 校验失败限次重下）</summary>
    private async Task DownloadSingleAsync(
        string url, string destPath, string? expectedSha1, long? expectedSize,
        DownloadProgressHandler? progress, CancellationToken ct, int attemptsLeft = 1)
    {
        var from = File.Exists(destPath) ? new FileInfo(destPath).Length : 0;

        // 416 防御：残留文件长度已 >= 目标总长（内容错误）→ 删除重下
        if (from > 0 && expectedSize is { } size && from >= size)
        {
            File.Delete(destPath);
            from = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _sourceMapper.Map(url));
        if (from > 0) request.Headers.Range = new RangeHeaderValue(from, null);

        using var response = await SendWith416RetryAsync(request, destPath, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0; // 本次响应要读的字节数
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        {
            using var dst = new FileStream(destPath, FileMode.Append, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                progress?.Invoke(new DownloadProgress("", Path.GetFileName(destPath), read, total,
                    total > 0 ? read * 100.0 / total : 0));
            }
            await dst.FlushAsync(ct);
        }

        // 校验：SHA1 优先，无 SHA1 时校验大小
        var ok = expectedSha1 is null
            ? expectedSize is null || new FileInfo(destPath).Length == expectedSize
            : await Sha1MatchesAsync(destPath, expectedSha1, ct);
        if (!ok)
        {
            File.Delete(destPath);
            if (attemptsLeft > 0)
                await DownloadSingleAsync(url, destPath, expectedSha1, expectedSize, progress, ct, attemptsLeft - 1);
            else
                throw new InvalidDataException($"下载校验失败（SHA1/大小不匹配）: {url}");
        }
    }

    /// <summary>发送请求；416（Range 起点不可满足）时删除文件从零重下一次</summary>
    private async Task<HttpResponseMessage> SendWith416RetryAsync(HttpRequestMessage request, string destPath, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(destPath);
            var retry = new HttpRequestMessage(HttpMethod.Get, request.RequestUri!);
            return await _http.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct);
        }
    }

    /// <summary>多连接 Range 分片下载：分片并行（单片重试 1 次）→ 合并 → SHA1 校验；整体失败回退单连接</summary>
    private async Task DownloadChunkedAsync(
        string url, string destPath, long totalSize, string? expectedSha1,
        DownloadProgressHandler? progress, CancellationToken ct)
    {
        try
        {
            var partDir = destPath + ".parts";
            Directory.CreateDirectory(partDir);

            var chunkSize = totalSize / ChunkCount;
            var downloadedBytes = 0L;

            var tasks = new List<Task>();
            for (var i = 0; i < ChunkCount; i++)
            {
                var start = i * chunkSize;
                var end = i == ChunkCount - 1 ? totalSize - 1 : start + chunkSize - 1;
                var partPath = Path.Combine(partDir, $"{i}.part");
                var expectedLen = end - start + 1;

                // 已完成段直接复用
                if (File.Exists(partPath) && new FileInfo(partPath).Length == expectedLen)
                {
                    Interlocked.Add(ref downloadedBytes, expectedLen);
                    continue;
                }

                tasks.Add(Task.Run(async () =>
                {
                    await DownloadChunkAsync(url, partPath, start, end, ct);
                    Interlocked.Add(ref downloadedBytes, expectedLen);
                    progress?.Invoke(new DownloadProgress("", Path.GetFileName(destPath), downloadedBytes, totalSize,
                        totalSize > 0 ? downloadedBytes * 100.0 / totalSize : 0));
                }, ct));
            }
            await Task.WhenAll(tasks);

            // 合并
            await using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                for (var i = 0; i < ChunkCount; i++)
                {
                    var partPath = Path.Combine(partDir, $"{i}.part");
                    await using var part = File.OpenRead(partPath);
                    await part.CopyToAsync(dst, ct);
                }
            }
            Directory.Delete(partDir, true);

            // SHA1 终校验，失败回退单连接重下
            if (expectedSha1 is not null && !await Sha1MatchesAsync(destPath, expectedSha1, ct))
            {
                File.Delete(destPath);
                await DownloadSingleAsync(url, destPath, expectedSha1, totalSize, progress, ct);
            }
        }
        catch
        {
            // 分片阶段失败：清理残留，回退单连接（弱网/镜像内容差异自愈）
            try { Directory.Delete(destPath + ".parts", true); } catch { }
            try { File.Delete(destPath); } catch { }
            await DownloadSingleAsync(url, destPath, expectedSha1, totalSize, progress, ct);
        }
    }

    private async Task DownloadChunkAsync(string url, string partPath, long start, long end, CancellationToken ct, int attempt = 0)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _sourceMapper.Map(url));
            request.Headers.Range = new RangeHeaderValue(start, end);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await src.CopyToAsync(dst, ct);
        }
        catch when (attempt < 1)
        {
            // 单片瞬时失败重试 1 次
            await DownloadChunkAsync(url, partPath, start, end, ct, attempt + 1);
        }
    }

    private async Task<long> GetContentLengthAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, _sourceMapper.Map(url));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return response.Content.Headers.ContentLength ?? 0;
    }

    private static async Task<bool> Sha1MatchesAsync(string path, string expected, CancellationToken ct)
    {
        try
        {
            await using var fs = File.OpenRead(path);
            var hash = await SHA1.HashDataAsync(fs, ct);
            return Convert.ToHexStringLower(hash) == expected;
        }
        catch (Exception) { return false; }
    }

    // ---------- 版本编排 ----------

    /// <summary>
    /// 编排完整版本下载：client jar → libraries（含 natives classifier）→ assets index → assets 差量 → logging。
    /// 进度报告：阶段文字 + 当前文件 + 文件级字节 + 整体百分比（按预估总字节加权，跨文件累计）。
    /// </summary>
    public async Task DownloadVersionAsync(
        VersionJson version, DownloadProgressHandler? progress = null, CancellationToken ct = default)
    {
        // 加载器版本：解析 inheritsFrom 链（父版本必须已安装；client jar 沿链继承后落子版本目录）
        if (version.InheritsFrom is not null)
        {
            version = VersionJsonMerger.ResolveChain(version, LoadParentJson);
            if (version.InheritsFrom is { } unresolved)
                throw new FileNotFoundException(
                    $"依赖的父版本 {unresolved} 未安装（请先在版本页安装原版 {unresolved}）");
        }

        var versionDir = Path.Combine(_gameDirectory, "versions", version.Id);
        var librariesDir = Path.Combine(_gameDirectory, "libraries");
        var assetsDir = Path.Combine(_gameDirectory, "assets");

        // 预估总字节（整体百分比分母）
        var librariesBytes = 0L;
        foreach (var lib in version.Libraries ?? [])
        {
            librariesBytes += lib.Downloads?.Artifact?.Size ?? 0;
            if (lib.Downloads?.Classifiers is { } classifiers)
                librariesBytes += classifiers.Values.Sum(c => c.Size ?? 0);
        }
        var assetsBytes = version.AssetIndex?.TotalSize ?? 0;
        var estimated = (version.Downloads?.Client?.Size ?? 0) + librariesBytes
                        + (version.AssetIndex?.Size ?? 0) + assetsBytes
                        + (version.Logging?.Client?.File?.Size ?? 0);

        // 文件级进度包装：阶段 + 文件名 + 整体百分比（跨文件累计；并发报告为近似值，可接受）
        var accumulated = 0L;
        DownloadProgressHandler? Wrap(string stage, string? fileName)
        {
            if (progress is null) return null;
            long fileDone = 0;
            return p =>
            {
                if (p.FileBytesDone > fileDone) fileDone = p.FileBytesDone;
                var overall = estimated > 0 ? (accumulated + fileDone) * 100.0 / estimated : p.OverallPercent;
                progress(new DownloadProgress(stage, fileName, p.FileBytesDone, p.FileTotalBytes, overall));
            };
        }

        // 1. client jar
        if (version.Downloads?.Client is { } client)
        {
            await DownloadFileAsync(client.Url, Path.Combine(versionDir, $"{version.Id}.jar"),
                client.Sha1, client.Size, Wrap("下载客户端", $"{version.Id}.jar"), ct);
            accumulated += client.Size ?? 0;
        }

        // 2. libraries（文件级并行 4，逐文件报告）
        using var semaphore = new SemaphoreSlim(4);
        var libraryTasks = new List<Task>();
        var libTotal = 0;
        foreach (var lib in version.Libraries ?? [])
        {
            if (lib.Downloads?.Artifact is not null) libTotal++;
            if (lib.Natives is not null) libTotal++;
        }
        var libIndex = 0;
        foreach (var lib in version.Libraries ?? [])
        {
            var artifact = lib.Downloads?.Artifact;
            if (artifact is not null)
            {
                var path = Path.Combine(librariesDir, MavenPath.FullPath(lib.Name));
                libraryTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var n = Interlocked.Increment(ref libIndex);
                        await DownloadFileAsync(artifact.Url, path, artifact.Sha1, artifact.Size,
                            Wrap($"下载库文件 {n}/{libTotal}", MavenPath.FileName(lib.Name)), ct);
                    }
                    finally { semaphore.Release(); }
                }, ct));
            }

            if (lib.Natives is { } natives && natives.TryGetValue("windows", out var classifierKey)
                && lib.Downloads?.Classifiers?.TryGetValue(classifierKey, out var nativeFile) == true)
            {
                var nativeName = MavenPath.FileName(lib.Name + ":" + classifierKey);
                var nativePath = Path.Combine(librariesDir, MavenPath.DirectoryPath(lib.Name), nativeName);
                libraryTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var n = Interlocked.Increment(ref libIndex);
                        await DownloadFileAsync(nativeFile.Url, nativePath, nativeFile.Sha1, nativeFile.Size,
                            Wrap($"下载库文件 {n}/{libTotal}", nativeName), ct);
                    }
                    finally { semaphore.Release(); }
                }, ct));
            }
        }
        await Task.WhenAll(libraryTasks);
        accumulated += librariesBytes;

        // 3. assets index
        if (version.AssetIndex is { } assetIndex)
        {
            var indexPath = Path.Combine(assetsDir, "indexes", $"{assetIndex.Id}.json");
            await DownloadFileAsync(assetIndex.Url, indexPath, assetIndex.Sha1, assetIndex.Size,
                Wrap("下载资源索引", $"{assetIndex.Id}.json"), ct);
            accumulated += assetIndex.Size ?? 0;

            // 4. assets 差量（文件级并行 8，按完成数报进度）
            if (File.Exists(indexPath))
            {
                var index = JsonSerializer.Deserialize<AssetsIndex>(
                    await File.ReadAllTextAsync(indexPath, ct));
                var objectsDir = Path.Combine(assetsDir, "objects");
                using var assetSemaphore = new SemaphoreSlim(8);
                var assetTasks = new List<Task>();
                var totalAssets = index.Objects.Count;
                var doneAssets = 0;
                foreach (var (hash, obj) in index.Objects)
                {
                    var objPath = Path.Combine(objectsDir, hash[..2], hash);
                    if (File.Exists(objPath) && new FileInfo(objPath).Length == obj.Size)
                    {
                        Interlocked.Increment(ref doneAssets);
                        continue;
                    }
                    var url = $"https://resources.download.minecraft.net/{hash[..2]}/{hash}";
                    assetTasks.Add(Task.Run(async () =>
                    {
                        await assetSemaphore.WaitAsync(ct);
                        try
                        {
                            await DownloadFileAsync(url, objPath, hash, obj.Size,
                                Wrap($"下载资源 {Volatile.Read(ref doneAssets)}/{totalAssets}", hash), ct);
                            var n = Interlocked.Increment(ref doneAssets);
                            if (progress is not null)
                                progress(new DownloadProgress($"下载资源 {n}/{totalAssets}", hash, n, totalAssets,
                                    estimated > 0
                                        ? (accumulated + (long)((double)n / totalAssets * assetsBytes)) * 100.0 / estimated
                                        : 0));
                        }
                        finally { assetSemaphore.Release(); }
                    }, ct));
                }
                await Task.WhenAll(assetTasks);
                accumulated += assetsBytes;
            }
        }

        // 5. logging 配置
        if (version.Logging?.Client?.File is { } logFile)
        {
            var fileName = Path.GetFileName(new Uri(logFile.Url).LocalPath);
            var logPath = Path.Combine(assetsDir, "log_configs", fileName);
            await DownloadFileAsync(logFile.Url, logPath, logFile.Sha1, logFile.Size,
                Wrap("日志配置", fileName), ct);
            accumulated += logFile.Size ?? 0;
        }
    }

    /// <summary>读磁盘上的父版本 JSON（inheritsFrom 链用）</summary>
    private VersionJson? LoadParentJson(string id)
    {
        var path = Path.Combine(_gameDirectory, "versions", id, $"{id}.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(path)); }
        catch (Exception) { return null; }
    }

    private sealed record AssetsIndex(
        [property: System.Text.Json.Serialization.JsonPropertyName("objects")]
        Dictionary<string, AssetObject> Objects);

    private sealed record AssetObject(
        [property: System.Text.Json.Serialization.JsonPropertyName("hash")] string Hash,
        [property: System.Text.Json.Serialization.JsonPropertyName("size")] long Size);
}
