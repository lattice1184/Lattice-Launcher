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
    /// <summary>同目标文件并发下载锁（同一 destPath 串行——避免并发写同一 jar 写坏）</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new();

    // 256KB 以上走 8 连接分片：国内直连 Modrinth CDN 单连接被限速（几十 KB/s），
    // 多连接分片可显著提速；弱网分片失败自动回退单连接（DownloadChunkedAsync catch）
    private const long ChunkThreshold = 256 * 1024;

    private readonly HttpClient _http;
    private readonly IDlSourceResolver _resolver;
    private readonly DownloadOptions _options;
    private readonly string _gameDirectory;
    private readonly SourceStats _sourceStats = new();
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<bool>> _networkChecker;

    public DownloadService(HttpClient? http = null, IDlSourceMapper? sourceMapper = null, string? gameDirectory = null)
        : this(http, sourceMapper is null ? ResolvingDlSourceMapper.Default : new ResolvingDlSourceMapper(sourceMapper),
            null, gameDirectory)
    {
    }

    public DownloadService(HttpClient? http, IDlSourceResolver? resolver, DownloadOptions? options, string? gameDirectory,
        Func<IReadOnlyList<string>, CancellationToken, Task<bool>>? networkChecker = null)
    {
        _http = http ?? CreateClient();
        _resolver = resolver ?? ResolvingDlSourceMapper.Default;
        _options = options ?? DownloadOptions.FromSettings(LauncherSettings.Current);
        _gameDirectory = gameDirectory ?? GameDirectory.Detect();
        _limitPerStream = _options.BytesPerSecond > 0
            ? Math.Max(_options.BytesPerSecond / Math.Max(_options.ChunkCount, 1), 8192)
            : 0;
        _networkChecker = networkChecker
            ?? ((hosts, ct) => NetworkChecker.CheckAsync(hosts, TimeSpan.FromSeconds(3), ct));
    }

    /// <summary>每流限速配额（总限速均分到并发流；每流独立累加器 → 总吞吐=设定值）</summary>
    private readonly long _limitPerStream;

    /// <summary>每流限速状态（独立累加器——流间不互相拖累，总吞吐=设定值）</summary>
    private sealed class ThrottleState
    {
        public long Bytes;
        public readonly System.Diagnostics.Stopwatch Sw = System.Diagnostics.Stopwatch.StartNew();
    }

    /// <summary>
    /// 分片进度共享上报（AL31）：各分片 Interlocked 累加已读字节 + 抢占式节流——
    /// 旧实现每片完成才 Invoke 一次，大文件进度/速度文字每片周期才刷新（观感延迟）。
    /// CompareExchange 抢占：同一时刻最多一个分片触发上报，节流窗口内最多报一次。
    /// </summary>
    private sealed class ChunkProgress
    {
        public long Bytes;
        public long LastReportMs;
        /// <summary>已上报的最大进度（ReportOnce 护栏：只允许递增上报，杜绝快照读+晚 Invoke 的倒序）</summary>
        public long Reported;
        public readonly System.Diagnostics.Stopwatch Sw = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>节流窗口（毫秒）；下载任务 UI 刷新间隔在此内不可感知，且避免高频 Post</summary>
        public const long WindowMs = 250;
    }

    /// <summary>每流限速节流：每 64KB 结算一次，超出配额则等待</summary>
    private static async Task ThrottleStreamAsync(int n, CancellationToken ct, ThrottleState st, long limit)
    {
        if (limit <= 0) return;
        st.Bytes += n;
        if (st.Bytes >= 65536)
        {
            var target = (double)st.Bytes / limit;
            var elapsed = st.Sw.Elapsed.TotalSeconds;
            if (elapsed < target)
                await Task.Delay(TimeSpan.FromSeconds(target - elapsed), ct);
            st.Bytes = 0;
            st.Sw.Restart();
        }
    }

    private static HttpClient CreateClient()
    {
        // 连接建立 5s 超时（AL32 秒接：原 15s——国内直连官方源常卡 TCP/TLS 握手，配合并行竞速
        // 慢源 5s 内判死，不再等满 15s 才轮到镜像）；
        // 不设整体 Timeout——body 下载不受限（51MB 大文件慢网也要 1 分钟+，整体超时会误杀正常下载）
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) };
        var client = new HttpClient(handler);
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
        // 同目标串行（并发任务下载同一 jar 时避免互相覆盖/写坏）
        var fileLock = FileLocks.GetOrAdd(destPath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(ct);
        try
        {
            await DownloadFileCoreAsync(url, destPath, expectedSha1, expectedSize, progress, ct);
        }
        finally
        {
            fileLock.Release();
        }
    }

    private async Task DownloadFileCoreAsync(
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

        // 候选源：按下载源策略排（官方优先=官方+镜像按速度排；镜像优先=镜像固定在前；仅镜像=只要镜像）
        var resolved = _resolver.Resolve(url);
        var candidates = _options.DownloadSource switch
        {
            DownloadSourcePreference.MirrorFirst => resolved.Count > 1 ? [resolved[1], resolved[0]] : resolved,
            DownloadSourcePreference.MirrorOnly => resolved.Count > 1 ? [resolved[1]] : resolved,
            _ => _sourceStats.Rank(resolved), // OfficialFirst：官方+镜像按历史速度排序（最快优先）
        };
        var backoff = _options.BackoffProvider ?? RetryPolicy.Backoff;
        Exception? last = null;

        for (var attempt = 0; attempt < _options.MaxSourceAttempts; attempt++)
        {
            if (candidates.Count == 1)
            {
                // 单候选（不可映射 URL）：走直接路径——保留断点续传（dest.tmp 预写 → Range 续传）
                // 与原子 rename 语义；竞速只用于多源场景
                try
                {
                    await DownloadFromSourceAsync(candidates[0], destPath, expectedSha1, expectedSize, progress, ct);
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
                {
                    last = ex;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // AL34：HttpClient.Timeout（默认 100s 等响应头）超时抛 TaskCanceledException——源级故障，
                    // 不是用户取消。此前漏出 → 叶子任务误判"已取消"（无错误、UI 不可重试、文件缺失）；
                    // 实机 08-09 探针 asm-9.10.1.jar（maven.fabricmc.net 单候选）即此。转可重试错误走退避下一轮。
                    last = new HttpRequestException("等待响应头超时（>100s）", null);
                }
                catch (OperationCanceledException) { throw; } // 用户取消原样上抛
                if (attempt < _options.MaxSourceAttempts - 1)
                {
                    var delay = backoff(attempt);
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
                }
                continue;
            }
            // AL32 并行竞速（秒接）：一轮内所有候选源同时发起，先到先得——官方卡 5s 超时时
            // 镜像已在同步下载，不再串行等满一轮（旧实现最坏 2 轮×2 源×5~15s）。
            // 每源独立 race 目标（destPath.race{i}）→ 中间文件（.tmp/.parts）天然隔离；
            // 首个校验通过的源赢：rename 到真名，取消其余源并清理残留。
            for (var i = 0; i < candidates.Count; i++) CleanupRaceFiles(destPath, i); // 上次崩溃残留保底
            using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pending = new List<(int Index, string Src, Task<(bool Ok, Exception? Error)> Task)>();
            for (var i = 0; i < candidates.Count; i++)
            {
                var idx = i;
                var src = candidates[i];
                pending.Add((idx, src, Task.Run(() => RaceOneAsync(idx, src, destPath,
                    expectedSha1, expectedSize, progress, raceCts.Token, ct), ct)));
            }
            Exception? raceLast = null;
            var won = false;
            while (pending.Count > 0)
            {
                var done = await Task.WhenAny(pending.Select(p => p.Task));
                var entry = pending.First(p => p.Task == done);
                pending.Remove(entry);
                var (ok, err) = await done;
                if (ok)
                {
                    raceCts.Cancel(); // 其余源取消
                    // 等取消中的任务真正停下再清理——否则边写边删会留未观察异常
                    foreach (var p in pending) { try { await p.Task; } catch { } }
                    foreach (var p in pending) CleanupRaceFiles(destPath, p.Index);
                    File.Move($"{destPath}.race{entry.Index}", destPath, true); // 赢家 → 真名
                    won = true;
                    break;
                }
                if (err is not null) raceLast = err;
            }
            if (won) return;
            last = raceLast ?? last;
            if (attempt < _options.MaxSourceAttempts - 1)
            {
                var delay = backoff(attempt);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            }
        }

        // 重试耗尽：检查网络并报告（用户要求"重试 3 次后检查网络并报告"）
        var hosts = candidates.Select(c => new Uri(c).Host).Distinct().ToList();
        var reachable = await _networkChecker(hosts, ct);
        if (!reachable)
            throw new InvalidOperationException(
                $"网络不可达：{string.Join("、", hosts)} 均无法连接，请检查网络/代理/防火墙（已重试 {_options.MaxSourceAttempts} 轮）");
        throw last ?? new InvalidOperationException($"下载失败: {url}");
    }

    /// <summary>
    /// 竞速单个候选源（AL32）：下载到独立 race 目标（隔离 .tmp/.parts），
    /// 校验通过返回成功；竞速输（被取消）或失败返回失败标记——取消不抛（赢家已定）。
    /// </summary>
    private async Task<(bool Ok, Exception? Error)> RaceOneAsync(
        int index, string url, string destPath, string? expectedSha1, long? expectedSize,
        DownloadProgressHandler? progress, CancellationToken raceCt, CancellationToken ct)
    {
        var raceDest = $"{destPath}.race{index}";
        try
        {
            await DownloadFromSourceAsync(url, raceDest, expectedSha1, expectedSize, progress, raceCt);
            return (true, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 竞速输（raceCts 已取消）或源自身超时——静默，赢家已经定了
            return (false, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
        {
            return (false, ex);
        }
    }

    /// <summary>清理某源的竞速残留（.race{i} 本体 + .tmp + .parts 目录）</summary>
    private static void CleanupRaceFiles(string destPath, int index)
    {
        var raceDest = $"{destPath}.race{index}";
        try { File.Delete(raceDest + ".tmp"); } catch { }
        try { Directory.Delete(raceDest + ".parts", true); } catch { }
        try { File.Delete(raceDest); } catch { }
    }

    /// <summary>单个候选源：定长走分片，否则单连接；前后计时记入源质量统计</summary>
    private async Task DownloadFromSourceAsync(
        string url, string destPath, string? expectedSha1, long? expectedSize,
        DownloadProgressHandler? progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var totalSize = expectedSize ?? await GetContentLengthAsync(url, ct);
        try
        {
            if (totalSize >= ChunkThreshold)
                await DownloadChunkedAsync(url, destPath, totalSize, expectedSha1, progress, ct);
            else
                await DownloadSingleAsync(url, destPath, expectedSha1, totalSize, progress, ct);
            sw.Stop();
            _sourceStats.RecordSuccess(url, totalSize, sw.ElapsedMilliseconds);
        }
        catch
        {
            _sourceStats.RecordFailure(url);
            throw;
        }
    }

    /// <summary>单连接下载（断点续传 + 416 防御 + 校验失败抛 InvalidDataException 由外层换源）。
    /// AL29 H1：写入一律走 destPath+".tmp"，校验通过后原子 rename——崩溃/断电残留只可能是 .tmp，
    /// 不会出现「File.Exists 通过但内容半截」的 destPath。</summary>
    private async Task DownloadSingleAsync(
        string url, string destPath, string? expectedSha1, long? expectedSize,
        DownloadProgressHandler? progress, CancellationToken ct)
    {
        var tmp = destPath + ".tmp";
        var from = File.Exists(tmp) ? new FileInfo(tmp).Length : 0;

        // 416 防御：残留文件长度已 >= 目标总长（内容错误）→ 删除重下
        if (from > 0 && expectedSize is { } size && from >= size)
        {
            File.Delete(tmp);
            from = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (from > 0) request.Headers.Range = new RangeHeaderValue(from, null);

        using var response = await SendWith416RetryAsync(request, destPath, ct);
        response.EnsureSuccessStatusCode();

        // AL8：进度 total 用真实目标大小（expectedSize 优先）——源返回 1B 垃圾（WAF 拦截页等）
        // 时不再显示 "1 B" 误导；校验仍由 sha1/size 兜底，无效响应自动换源
        var total = expectedSize ?? response.Content.Headers.ContentLength ?? 0;
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        {
            using var dst = new FileStream(tmp, FileMode.Append, FileAccess.Write, FileShare.None);
            var buffer = new byte[_options.BufferSize];
            long read = 0;
            var throttle = new ThrottleState();
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                await ThrottleStreamAsync(n, ct, throttle, _limitPerStream);
                read += n;
                progress?.Invoke(new DownloadProgress("", Path.GetFileName(destPath), read, total,
                    total > 0 ? Math.Min(read * 100.0 / total, 99) : 0));
            }
            await dst.FlushAsync(ct);
        }

        // 校验：SHA1 优先，无 SHA1 时校验大小——校验对象是 tmp，通过后才替换真名
        var ok = expectedSha1 is null
            ? expectedSize is null || new FileInfo(tmp).Length == expectedSize
            : await Sha1MatchesAsync(tmp, expectedSha1, ct);
        if (!ok)
        {
            File.Delete(tmp);
            throw new InvalidDataException($"下载校验失败（SHA1/大小不匹配）: {url}");
        }
        // AL29 H1：同目录 tmp → 原子替换（同卷 rename），旧 destPath 在文件完整前不被触碰
        File.Move(tmp, destPath, true);
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
            File.Delete(destPath + ".tmp"); // AL29 H1：416 只删中间产物，destPath 未验证前不动
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
            // AL31：统一实时计数器——节流上报与片完成上报同源（片完成时 cp.Bytes 已含该片全部字节），
            // 避免旧 downloadedBytes（只含已完成分片）与实时字节打架导致进度回退
            var cp = new ChunkProgress();

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
                    Interlocked.Add(ref cp.Bytes, expectedLen);
                    continue;
                }

                tasks.Add(Task.Run(async () =>
                {
                    await DownloadChunkAsync(url, partPath, start, end, ct, cp, Path.GetFileName(destPath), totalSize, progress);
                    // 片完成即时上报（force：允许同值重复报，见 ReportOnce 注释）
                    ReportOnce(cp, Path.GetFileName(destPath), totalSize, progress, force: true);
                }, ct));
            }
            await Task.WhenAll(tasks);
            // 全片完成后补报最终值（片回调已覆盖时 Reported 护栏自动跳过，不重复）

            // 合并写 tmp（AL29 H1：完整校验通过前不落真名）
            var tmp = destPath + ".tmp";
            await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                for (var i = 0; i < chunkCount; i++)
                {
                    var partPath = Path.Combine(partDir, $"{i}.part");
                    await using var part = File.OpenRead(partPath);
                    await part.CopyToAsync(dst, ct);
                }
            }
            Directory.Delete(partDir, true);

            // SHA1 终校验失败 → 抛异常，外层换源重试（tmp 由 catch 清理）
            if (expectedSha1 is not null && !await Sha1MatchesAsync(tmp, expectedSha1, ct))
            {
                File.Delete(tmp);
                throw new InvalidDataException($"分片下载校验失败: {url}");
            }
            File.Move(tmp, destPath, true); // AL29 H1：校验通过后原子替换
        }
        catch
        {
            // 分片阶段失败：清理残留，回退单连接（弱网/镜像内容差异自愈）。
            // AL29 H1：只清中间产物（.parts/.tmp），destPath 已有旧文件保持不动——新文件未验证不覆盖
            try { Directory.Delete(destPath + ".parts", true); } catch { }
            try { File.Delete(destPath + ".tmp"); } catch { }
            await DownloadSingleAsync(url, destPath, expectedSha1, totalSize, progress, ct);
        }
    }

    private async Task DownloadChunkAsync(string url, string partPath, long start, long end, CancellationToken ct,
        ChunkProgress? cp = null, string? destName = null, long totalSize = 0,
        DownloadProgressHandler? progress = null, int attempt = 0)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(start, end);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[_options.BufferSize];
            var throttle = new ThrottleState();
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                await ThrottleStreamAsync(n, ct, throttle, _limitPerStream);
                if (cp is not null && progress is not null)
                    ReportChunkProgress(cp, n, destName!, totalSize, progress);
            }
        }
        catch when (attempt < 1)
        {
            // 单片瞬时失败重试 1 次
            await DownloadChunkAsync(url, partPath, start, end, ct, cp, destName, totalSize, progress, attempt + 1);
        }
    }

    /// <summary>分片进度节流上报：Interlocked 累加字节，CompareExchange 抢占 250ms 窗口（见 ChunkProgress 注释）</summary>
    private static void ReportChunkProgress(ChunkProgress cp, int n, string destName, long totalSize, DownloadProgressHandler progress)
    {
        Interlocked.Add(ref cp.Bytes, n);
        var now = cp.Sw.ElapsedMilliseconds;
        var last = Interlocked.Read(ref cp.LastReportMs);
        if (now - last >= ChunkProgress.WindowMs
            && Interlocked.CompareExchange(ref cp.LastReportMs, now, last) == last)
        {
            ReportOnce(cp, destName, totalSize, progress);
        }
    }

    /// <summary>
    /// 串行上报护栏：锁内读 Bytes + 锁内 Invoke（锁串行化 → 读到的值序列必然不降，杜绝
    /// 「读旧快照 → 锁外晚 Invoke」的倒序回退）。force=false 时按 cp.Reported 去重（节流/最终
    /// 上报报最新值即可）；force=true 时允许同值重复报（片完成回调——片并行同刻完成时
    /// 若不重复报会被合并成 1 次，实时粒度丢失）。锁内 Invoke：用户回调仅更新 UI 进度，
    /// 不重入下载（若回调同步触发同 cp 下载会死锁——约定如此）。
    /// </summary>
    private static void ReportOnce(ChunkProgress cp, string destName, long totalSize,
        DownloadProgressHandler progress, bool force = false)
    {
        lock (cp)
        {
            var done = Volatile.Read(ref cp.Bytes);
            if (!force && done <= cp.Reported) return;
            cp.Reported = done;
            progress(new DownloadProgress("", destName, done, totalSize,
                totalSize > 0 ? Math.Min(done * 100.0 / totalSize, 99) : 0));
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
                var overall = estimated > 0 ? Math.Min((accumulated + fileDone) * 100.0 / estimated, 99) : p.OverallPercent;
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
            else if (lib.Url is not null) libTotal++; // AL10.1：Fabric/Forge 的 url 形式库（顶层 url 无 downloads.artifact）
            if (lib.Natives is not null) libTotal++;
        }
        var libIndex = 0;
        foreach (var lib in version.Libraries ?? [])
        {
            var artifact = lib.Downloads?.Artifact;
            // AL30：url 空 artifact（forge client classifier 继承引用）无下载目标，跳过（同 pipeline/VerifyFiles 规则）
            if (artifact is not null && !string.IsNullOrEmpty(artifact.Url))
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

            // AL10.1：Fabric/Forge 库无 downloads.artifact，顶层 url + Maven 坐标拼下载地址（如 maven.fabricmc.net）
            if (artifact is null && lib.Url is { } repoUrl)
            {
                var path = Path.Combine(librariesDir, MavenPath.FullPath(lib.Name));
                var dlUrl = repoUrl.TrimEnd('/') + "/" + MavenPath.FullPath(lib.Name).Replace('\\', '/');
                libraryTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var n = Interlocked.Increment(ref libIndex);
                        await DownloadFileAsync(dlUrl, path, lib.Sha1, lib.Size,
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
                                        ? Math.Min((accumulated + (long)((double)n / totalAssets * assetsBytes)) * 100.0 / estimated, 99)
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
