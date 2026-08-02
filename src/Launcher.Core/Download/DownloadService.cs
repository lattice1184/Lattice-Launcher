using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Core.Model.Mojang;
using Launcher.Core.Utils;

namespace Launcher.Core.Download;

/// <summary>
/// 下载服务：大文件多连接 Range 分片并发 + 小文件单连接断点续传。
/// 外层换源回退：官方失败 → 镜像（BMCLAPI）→ 指数退避重试整轮；SHA1 校验 + 幂等 + 416 防御。
/// </summary>
public sealed class DownloadService
{
    private const long ChunkThreshold = 2 * 1024 * 1024; // 2MB 以上走分片

    private readonly HttpClient _http;
    private readonly IDlSourceResolver _resolver;
    private readonly DownloadOptions _options;
    private readonly string _gameDirectory;

    public DownloadService(HttpClient? http = null, IDlSourceMapper? sourceMapper = null, string? gameDirectory = null)
        : this(http, sourceMapper is null ? ResolvingDlSourceMapper.Default : new ResolvingDlSourceMapper(sourceMapper),
            null, gameDirectory)
    {
    }

    public DownloadService(HttpClient? http, IDlSourceResolver? resolver, DownloadOptions? options, string? gameDirectory)
    {
        _http = http ?? CreateClient();
        _resolver = resolver ?? ResolvingDlSourceMapper.Default;
        _options = options ?? DownloadOptions.Default;
        _gameDirectory = gameDirectory ?? GameDirectory.Detect();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YanKa-Launcher/0.1");
        return client;
    }

    /// <summary>
    /// 下载文件。外层循环：每轮遍历候选源（官方→镜像），全失败后指数退避进入下一轮。
    /// 校验失败（InvalidDataException）与网络错误（HttpRequestException）都触发换源。
    /// </summary>
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

        var sources = _resolver.Resolve(url);
        var backoff = _options.BackoffProvider ?? RetryPolicy.Backoff;
        Exception? last = null;

        for (var attempt = 0; attempt < _options.MaxSourceAttempts; attempt++)
        {
            foreach (var src in sources)
            {
                try
                {
                    await DownloadFromSourceAsync(src, destPath, expectedSha1, expectedSize, progress, ct);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
                {
                    last = ex;
                }
            }
            if (attempt < _options.MaxSourceAttempts - 1)
            {
                var delay = backoff(attempt);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            }
        }
        throw last ?? new InvalidOperationException($"下载失败: {url}");
    }

    /// <summary>单个候选源：定长走分片，否则单连接</summary>
    private async Task DownloadFromSourceAsync(
        string url, string destPath, string? expectedSha1, long? expectedSize,
        DownloadProgressHandler? progress, CancellationToken ct)
    {
        var totalSize = expectedSize ?? await GetContentLengthAsync(url, ct);

        if (totalSize >= ChunkThreshold)
            await DownloadChunkedAsync(url, destPath, totalSize, expectedSha1, progress, ct);
        else
            await DownloadSingleAsync(url, destPath, expectedSha1, totalSize, progress, ct);
    }

    /// <summary>单连接下载（断点续传 + 416 防御 + 校验失败抛 InvalidDataException 由外层换源）</summary>
    private async Task DownloadSingleAsync(
        string url, string destPath, string? expectedSha1, long? expectedSize,
        DownloadProgressHandler? progress, CancellationToken ct)
    {
        var from = File.Exists(destPath) ? new FileInfo(destPath).Length : 0;

        // 416 防御：残留文件长度已 >= 目标总长（内容错误）→ 删除重下
        if (from > 0 && expectedSize is { } size && from >= size)
        {
            File.Delete(destPath);
            from = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
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

            var chunkCount = Math.Max(1, _options.ChunkCount);
            var chunkSize = totalSize / chunkCount;
            var downloadedBytes = 0L;

            var tasks = new List<Task>();
            for (var i = 0; i < chunkCount; i++)
            {
                var start = i * chunkSize;
                var end = i == chunkCount - 1 ? totalSize - 1 : start + chunkSize - 1;
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
                for (var i = 0; i < chunkCount; i++)
                {
                    var partPath = Path.Combine(partDir, $"{i}.part");
                    await using var part = File.OpenRead(partPath);
                    await part.CopyToAsync(dst, ct);
                }
            }
            Directory.Delete(partDir, true);

            // SHA1 终校验失败 → 抛异常，外层换源重试
            if (expectedSha1 is not null && !await Sha1MatchesAsync(destPath, expectedSha1, ct))
            {
                File.Delete(destPath);
                throw new InvalidDataException($"分片下载校验失败: {url}");
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
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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

    /// <summary>HEAD 取长度：试全部候选源，全失败返回 0（走单连接按响应长度下载）</summary>
    private async Task<long> GetContentLengthAsync(string url, CancellationToken ct)
    {
        foreach (var src in _resolver.Resolve(url))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, src);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                return response.Content.Headers.ContentLength ?? 0;
            }
            catch (Exception) { /* 试下一候选源 */ }
        }
        return 0;
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
    /// 编排完整版本下载。传 ctx（组任务）走阶段全并行管线（VersionDownloadPipeline，文件级子任务）；
    /// 否则走旧展平路径（阶段串行 + 加权整体百分比，兼容旧调用与测试）。
    /// </summary>
    public Task DownloadVersionAsync(
        VersionJson version, DownloadGroupContext? ctx = null,
        DownloadProgressHandler? progress = null, CancellationToken ct = default)
    {
        if (ctx is not null)
            return new VersionDownloadPipeline(this, _options, _gameDirectory).RunAsync(version, ctx, ct);
        return RunLegacyAsync(version, progress, ct);
    }

    /// <summary>旧展平路径：client → libraries → index → assets → logging（阶段串行）</summary>
    public async Task RunLegacyAsync(
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

        // 2. libraries（文件级并行，逐文件报告）
        using var semaphore = new SemaphoreSlim(_options.LibraryConcurrency);
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

            // 4. assets 差量（文件级并行，按完成数报进度）
            if (File.Exists(indexPath))
            {
                var index = JsonSerializer.Deserialize<AssetsIndex>(
                    await File.ReadAllTextAsync(indexPath, ct));
                if (index is not null)
                {
                var objectsDir = Path.Combine(assetsDir, "objects");
                using var assetSemaphore = new SemaphoreSlim(_options.AssetConcurrency);
                var assetTasks = new List<Task>();
                var totalAssets = index.Objects.Count;
                var doneAssets = 0;
                foreach (var (_, obj) in index.Objects)   // key 是文件路径，hash 在 value
                {
                    var h = obj.Hash;
                    var objPath = Path.Combine(objectsDir, h[..2], h);
                    if (File.Exists(objPath) && new FileInfo(objPath).Length == obj.Size)
                    {
                        Interlocked.Increment(ref doneAssets);
                        continue;
                    }
                    var url = $"https://resources.download.minecraft.net/{h[..2]}/{h}";
                    assetTasks.Add(Task.Run(async () =>
                    {
                        await assetSemaphore.WaitAsync(ct);
                        try
                        {
                            await DownloadFileAsync(url, objPath, h, obj.Size,
                                Wrap($"下载资源 {Volatile.Read(ref doneAssets)}/{totalAssets}", h), ct);
                            var n = Interlocked.Increment(ref doneAssets);
                            if (progress is not null)
                                progress(new DownloadProgress($"下载资源 {n}/{totalAssets}", h, n, totalAssets,
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
