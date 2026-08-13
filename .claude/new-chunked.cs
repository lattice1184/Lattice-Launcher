    /// <summary>多连接 Range 分片下载：固定片大小 + 并发调度（并发 = gate 槽位；升片 = 提高并发不清进度）
    /// → 合并 → 总长/SHA1 校验；整体失败回退单连接</summary>
    private async Task DownloadChunkedAsync(
        string url, string destPath, long totalSize, string? expectedSha1,
        DownloadProgressHandler? progress, CancellationToken ct)
    {
        try
        {
            var partDir = destPath + ".parts";
            Directory.CreateDirectory(partDir);

            var maxChunks = Math.Max(1, _options.ChunkCount);
            // 8-18 固定片大小：边界永不变化 → 已完成片跨 attempt/换源/并发变化全部复用（换源续进度核心）。
            // 旧实现（totalSize/chunkCount）片数一变边界全变，旧 .part 全废——升片/重试必然从零重下。
            var totalChunks = Math.Max(1, (int)Math.Ceiling(totalSize / (double)FixedChunkSize));
            var currentConcurrency = Math.Clamp(await ProbeAndDecideConcurrencyAsync(url, totalSize, partDir, ct), 1, maxChunks);
            // 并发 = gate 槽位数（初始探测值，上限 maxChunks）；升片 = Release 腾出更多槽位，排队片自动进入
            using var gate = new SemaphoreSlim(currentConcurrency, maxChunks);
            var lastUpgradeAt = DateTime.MinValue;

            // AL61 分片总吞吐监测：cp.Bytes 每采样间隔测速，持续低速（默认 30s < 100KB/s）→ 判死换路；
            // 8-16 渐进限速 → 升片（只提高并发，不清 .parts 不重切——已下字节保留）；8-17 并发到顶仍慢 → 立即判死
            var slowDetector = new SlowSourceDetector(SlowThresholdForLimit(), _options.SlowSamples, _options.SlowProbeMs);
            using var slowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var slowAborted = 0;
            var speedRing = new double[3];
            var speedIdx = 0;
            var prevBytes = 0L;
            var cp = new ChunkProgress();
            var slowWatch = Task.Run(async () =>
            {
                try
                {
                    while (!slowCts.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(_options.SlowProbeMs), slowCts.Token);
                        var bytes = cp.Bytes;
                        speedRing[speedIdx % 3] = (bytes - prevBytes) / (TimeSpan.FromMilliseconds(_options.SlowProbeMs).TotalSeconds);
                        prevBytes = bytes;
                        speedIdx++;
                        if (slowDetector.ShouldAbort(bytes, slowCts.Token))
                        {
                            Volatile.Write(ref slowAborted, 1);
                            slowCts.Cancel();
                            break;
                        }
                        var avg = (speedRing[0] + speedRing[1] + speedRing[2]) / 3;
                        // 升片判定：3 采样均速 < 阈值、剩余够下、并发有余量、距上次升片 ≥10s
                        if (speedIdx >= 3 && ShouldUpgradeChunks(
                                avg, bytes, totalSize, currentConcurrency, maxChunks,
                                (DateTime.UtcNow - lastUpgradeAt).TotalSeconds))
                        {
                            lastUpgradeAt = DateTime.UtcNow;
                            var target = Math.Min(maxChunks, currentConcurrency * 2);
                            gate.Release(target - currentConcurrency);
                            currentConcurrency = target;
                        }
                        // 8-17 并发到顶仍慢 → 立即判死换路（镜像竞速淘汰后不会回来——外层重新 Resolve 让镜像重新参与）
                        if (speedIdx >= 3 && currentConcurrency >= maxChunks && avg < SlowThresholdForLimit())
                        {
                            Volatile.Write(ref slowAborted, 1);
                            slowCts.Cancel();
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }, ct);

            // 固定边界分片：已完成段复用入账（长度匹配即有效）、部分片入账后片内续传；未完成片排队等 gate
            var tasks = new List<Task>();
            for (var i = 0; i < totalChunks; i++)
            {
                var start = (long)i * FixedChunkSize;
                var end = Math.Min(start + FixedChunkSize - 1, totalSize - 1);
                var partPath = Path.Combine(partDir, $"{i}.part");
                var expectedLen = end - start + 1;

                // 已完成段直接复用（边界固定 → 跨 attempt/换源无缝续传）
                if (File.Exists(partPath) && new FileInfo(partPath).Length == expectedLen)
                {
                    Interlocked.Add(ref cp.Bytes, expectedLen);
                    continue;
                }

                // AL67 部分片（中断残留）：已下字节先入账（进度从断点续走不归零），
                // DownloadChunkAsync 内部从 have 处续传——片内重试不会再入账（重试不经过本循环）
                if (File.Exists(partPath) && new FileInfo(partPath).Length is > 0 and var have)
                    Interlocked.Add(ref cp.Bytes, have);

                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync(slowCts.Token);
                    try
                    {
                        await DownloadChunkAsync(url, partPath, start, end, slowCts.Token, cp, Path.GetFileName(destPath), totalSize, progress);
                        // 片完成即时上报（force：允许同值重复报，见 ReportOnce 注释）
                        ReportOnce(cp, Path.GetFileName(destPath), totalSize, progress, force: true);
                    }
                    finally { gate.Release(); }
                }, slowCts.Token));
            }
            try
            {
                await Task.WhenAll(tasks);
                // 分片全成功 → 立即停监测，不等自然判死（慢速阈值=0 时 slowWatch 永不退出——真机靠
                // 「速度归零判死」碰巧退出，阈值关闭/短任务时 await slowWatch 永挂）
                slowCts.Cancel();
                await slowWatch;
            }
            catch
            {
                if (Volatile.Read(ref slowAborted) == 1)
                    throw new SlowSourceException(_options.SlowSpeedBps, slowDetector.LastSpeed); // 源死：直接换路
                throw;
            }
            finally
            {
                slowCts.Cancel();
            }
            // 全片完成后补报最终值（片回调已覆盖时 Reported 护栏自动跳过，不重复）

            // 合并写 tmp（AL29 H1：完整校验通过前不落真名）
            var tmp = destPath + ".tmp";
            await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                for (var i = 0; i < totalChunks; i++)
                {
                    var partPath = Path.Combine(partDir, $"{i}.part");
                    // AL67 片长度校验：服务器忽略 Range 返回 200 全量时片超长——拒绝换路（否则错位文件落盘）
                    var partLen = new FileInfo(partPath).Length;
                    var expectLen = i == totalChunks - 1 ? totalSize - (long)i * FixedChunkSize : FixedChunkSize;
                    if (partLen != expectLen)
                        throw new InvalidDataException($"分片 {i} 长度异常（{partLen} != {expectLen}）: {url}");
                    await using var part = File.OpenRead(partPath);
                    await part.CopyToAsync(dst, ct);
                }
                // 8-18 总长度校验：无 sha1（第三方下载）时字节数一致性的最后兜底——防分片计算/源大小漂移
                if (dst.Length != totalSize)
                    throw new InvalidDataException($"合并后总长度不符（{dst.Length} != {totalSize}）: {url}");
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
        catch (SlowSourceException) { throw; } // AL61 源死：不回退单连接（还要再等 30s 才判死）——直接换路
        catch
        {
            // 分片阶段失败：清理残留，回退单连接（弱网/镜像内容差异自愈）。
            // AL29 H1：只清中间产物（.parts/.tmp），destPath 已有旧文件保持不动——新文件未验证不覆盖
            try { Directory.Delete(destPath + ".parts", true); } catch { }
            try { File.Delete(destPath + ".tmp"); } catch { }
            await DownloadSingleAsync(url, destPath, expectedSha1, totalSize, progress, ct);
        }
    }
