using System.Text.Json;
using Launcher.Core.Model.Mojang;
using Launcher.Core.Utils;

namespace Launcher.Core.Download;

/// <summary>
/// 版本下载编排（组任务、阶段全并行）：
/// 阶段 1：client jar / libraries（每文件子任务，并发门）/ assets index / logging 同时启动；
/// 阶段 2：assets 差量（依赖 index 完成）——单个计数子任务（2000+ 文件绝不建 2000 行）。
/// 每个文件是一个子任务 → 下载页可展开看独立进度；父任务按 Weight 加权聚合。
/// </summary>
public sealed class VersionDownloadPipeline
{
    private readonly DownloadService _downloads;
    private readonly DownloadOptions _options;
    private readonly string _gameDirectory;

    public VersionDownloadPipeline(DownloadService downloads, DownloadOptions options, string gameDirectory)
    {
        _downloads = downloads;
        _options = options;
        _gameDirectory = gameDirectory;
    }

    public async Task RunAsync(VersionJson version, DownloadGroupContext ctx, CancellationToken ct)
    {
        // 加载器版本：解析 inheritsFrom 链（父版本必须已安装）
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

        // ---- 阶段 1：全并行 ----
        var tasks = new List<Task>();

        // 1. client jar
        if (version.Downloads?.Client is { } client)
        {
            tasks.Add(ctx.AddChild($"{version.Id}.jar", client.Size ?? 0, (p, c) =>
                _downloads.DownloadFileAsync(client.Url, Path.Combine(versionDir, $"{version.Id}.jar"),
                    client.Sha1, client.Size, p, c)).Completion);
        }

        // 2. libraries（每库文件一个子任务，共享并发门——创建即排队，不阻塞编排）
        using var libGate = new SemaphoreSlim(_options.LibraryConcurrency);
        foreach (var lib in version.Libraries ?? [])
        {
            var artifact = lib.Downloads?.Artifact;
            if (artifact is not null)
            {
                var path = Path.Combine(librariesDir, MavenPath.FullPath(lib.Name));
                tasks.Add(ctx.AddChild(MavenPath.FileName(lib.Name), artifact.Size ?? 0, async (p, c) =>
                {
                    await libGate.WaitAsync(c);
                    try { await _downloads.DownloadFileAsync(artifact.Url, path, artifact.Sha1, artifact.Size, p, c); }
                    finally { libGate.Release(); }
                }).Completion);
            }

            if (lib.Natives is { } natives && natives.TryGetValue("windows", out var classifierKey)
                && lib.Downloads?.Classifiers?.TryGetValue(classifierKey, out var nativeFile) == true)
            {
                var nativeName = MavenPath.FileName(lib.Name + ":" + classifierKey);
                var nativePath = Path.Combine(librariesDir, MavenPath.DirectoryPath(lib.Name), nativeName);
                tasks.Add(ctx.AddChild(nativeName, nativeFile.Size ?? 0, async (p, c) =>
                {
                    await libGate.WaitAsync(c);
                    try { await _downloads.DownloadFileAsync(nativeFile.Url, nativePath, nativeFile.Sha1, nativeFile.Size, p, c); }
                    finally { libGate.Release(); }
                }).Completion);
            }

            // AL10.1：Fabric/Forge 库无 downloads.artifact，顶层 url + Maven 坐标拼下载地址（如 maven.fabricmc.net）
            if (artifact is null && lib.Url is { } repoUrl)
            {
                var path = Path.Combine(librariesDir, MavenPath.FullPath(lib.Name));
                var dlUrl = repoUrl.TrimEnd('/') + "/" + MavenPath.FullPath(lib.Name).Replace('\\', '/');
                tasks.Add(ctx.AddChild(MavenPath.FileName(lib.Name), lib.Size ?? 0, async (p, c) =>
                {
                    await libGate.WaitAsync(c);
                    try { await _downloads.DownloadFileAsync(dlUrl, path, lib.Sha1, lib.Size, p, c); }
                    finally { libGate.Release(); }
                }).Completion);
            }
        }

        // 3. assets index
        DownloadTask? indexChild = null;
        string? indexPath = null;
        if (version.AssetIndex is { } assetIndex)
        {
            indexPath = Path.Combine(assetsDir, "indexes", $"{assetIndex.Id}.json");
            indexChild = ctx.AddChild($"{assetIndex.Id}.json", assetIndex.Size ?? 0, (p, c) =>
                _downloads.DownloadFileAsync(assetIndex.Url, indexPath, assetIndex.Sha1, assetIndex.Size, p, c));
            tasks.Add(indexChild.Completion);
        }

        // 4. logging 配置
        if (version.Logging?.Client?.File is { } logFile)
        {
            var fileName = Path.GetFileName(new Uri(logFile.Url).LocalPath);
            var logPath = Path.Combine(assetsDir, "log_configs", fileName);
            tasks.Add(ctx.AddChild(fileName, logFile.Size ?? 0, (p, c) =>
                _downloads.DownloadFileAsync(logFile.Url, logPath, logFile.Sha1, logFile.Size, p, c)).Completion);
        }

        await Task.WhenAll(tasks);

        // ---- 阶段 2：assets 差量（index 完成后；单计数子任务） ----
        if (indexChild is not null && indexPath is not null && File.Exists(indexPath))
        {
            var missing = ReadMissingObjects(indexPath, assetsDir);
            if (missing.Count > 0)
            {
                var assetsWeight = missing.Sum(m => m.Size);
                var assetsChild = ctx.AddChild($"资源文件 ({missing.Count} 个)", assetsWeight,
                    (p, c) => DownloadAssetsBatchAsync(missing, assetsWeight, p, c));
                await assetsChild.Completion;
            }
        }
    }

    /// <summary>读 index 并计算缺失对象（已存在且大小匹配的跳过）。
    /// 注意：index 的 key 是文件路径（如 "minecraft/lang/zh_cn.json"），下载 hash 在 value 里。</summary>
    private List<(string Hash, long Size)> ReadMissingObjects(string indexPath, string assetsDir)
    {
        var index = JsonSerializer.Deserialize<AssetsIndex>(File.ReadAllText(indexPath));
        if (index is null) return [];
        var objectsDir = Path.Combine(assetsDir, "objects");
        var missing = new List<(string, long)>();
        foreach (var (_, obj) in index.Objects)
        {
            var objPath = Path.Combine(objectsDir, obj.Hash[..2], obj.Hash);
            if (File.Exists(objPath) && new FileInfo(objPath).Length == obj.Size) continue;
            missing.Add((obj.Hash, obj.Size));
        }
        return missing;
    }

    /// <summary>资源批量下载（文件级并行；计数报告：FileBytesDone 按权重缩放）</summary>
    private async Task DownloadAssetsBatchAsync(
        List<(string Hash, long Size)> missing, long assetsWeight,
        DownloadProgressHandler? progress, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(_options.AssetConcurrency);
        var total = missing.Count;
        var done = 0;
        var tasks = missing.Select(obj => Task.Run(async () =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var url = $"https://resources.download.minecraft.net/{obj.Hash[..2]}/{obj.Hash}";
                var path = Path.Combine(_gameDirectory, "assets", "objects", obj.Hash[..2], obj.Hash);
                await _downloads.DownloadFileAsync(url, path, obj.Hash, obj.Size, null, ct);
                var n = Interlocked.Increment(ref done);
                if (progress is not null)
                    progress(new DownloadProgress($"下载资源 {n}/{total}", obj.Hash,
                        assetsWeight * n / total, assetsWeight, n * 100.0 / total));
            }
            finally { gate.Release(); }
        }, ct)).ToList();
        await Task.WhenAll(tasks);
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
