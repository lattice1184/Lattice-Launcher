using System.Text.Json;
using Launcher.Core.Download;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Server;

/// <summary>
/// 服务端安装：从版本 JSON 的 downloads.server.url 下载 server.jar 到
/// {gameDir}/servers/{versionId}/，并写入 eula.txt（同意 EULA）。
/// </summary>
public sealed class ServerInstaller
{
    private readonly DownloadService _downloads;

    public ServerInstaller(DownloadService? downloads = null)
        => _downloads = downloads ?? new DownloadService();

    /// <summary>服务端目录（启动器目录树下 servers/{versionId}——AE3 归位，不在游戏目录内）</summary>
    public static string ServerDir(string gameDir, string versionId)
    {
        var parent = Path.GetDirectoryName(gameDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(string.IsNullOrEmpty(parent) ? gameDir : parent, "servers", versionId);
    }

    /// <summary>一次性迁移：旧位置 {gameDir}\servers → 启动器目录树 {父级}\servers（AE3；失败下次再试）</summary>
    public static void MigrateLegacy(string gameDir)
    {
        try
        {
            var old = Path.Combine(gameDir, "servers");
            if (!Directory.Exists(old)) return;
            var newRoot = ServerDir(gameDir, ""); // 取根
            if (Directory.EnumerateFileSystemEntries(old).Any() && !Directory.Exists(newRoot))
                Directory.Move(old, newRoot);
        }
        catch { /* 迁移失败不影响使用 */ }
    }

    /// <summary>从已装版本安装服务端 → server.jar 路径（幂等：已存在且大小正确则跳过）</summary>
    public async Task<string> InstallAsync(string versionId, string gameDir,
        DownloadProgressHandler? progress = null, CancellationToken ct = default)
    {
        var versionPath = Path.Combine(gameDir, "versions", versionId, $"{versionId}.json");
        if (!File.Exists(versionPath))
            throw new FileNotFoundException($"版本 {versionId} 未安装（请先在版本页下载）");

        var version = JsonSerializer.Deserialize<VersionJson>(await File.ReadAllTextAsync(versionPath, ct))
            ?? throw new InvalidDataException($"版本 JSON 解析失败: {versionId}");
        // 解析 inheritsFrom 链（Fabric/Forge 等加载器 profile 无 downloads.server，继承原版）——
        // 不解析则服务端 URL 永远拿不到，每次下载失败 → 开服无限套娃（AE1 根因）
        var merged = VersionJsonMerger.ResolveChain(version, id => LoadParent(gameDir, id));
        var serverUrl = merged.Downloads?.Server?.Url;
        if (string.IsNullOrEmpty(serverUrl))
        {
            // 加载器/整合包版本无 downloads.server——链接来自原版（如 1.21.1-Fabric → 1.21.1）
            throw new InvalidDataException(versionId.Contains('-')
                ? $"版本 {versionId} 的服务端链接来自原版 {versionId[..versionId.IndexOf('-')]}——请先安装该原版版本再开服"
                : $"版本 {versionId} 没有服务端下载链接（该版本不支持开服）");
        }
        // size 无效（缺失/≤0）时传 null——不校验大小，由 IsValidServerJar 兜底（AL3）
        long? expectedSize = merged.Downloads!.Server!.Size is { } sz && sz > 0 ? sz : null;

        var dir = ServerDir(gameDir, versionId);
        Directory.CreateDirectory(dir);
        var jarPath = Path.Combine(dir, "server.jar");
        // 候选下载链（AL2 三源）：官方 piston-data → launcher.mojang.com 旧域名 → BMCLAPI 镜像。
        // 官方直连国内不稳（"下载服务端基本失败"根因）；BMCLAPI 签名 CDN 也可能挂——逐源尝试直到成功。
        // AL3 彻查：候选 2/3 无 size 校验，BMCLAPI WAF 等可能返回 200 错误内容被当成功 → "缺少或过小"——
        // 每候选下载后统一校验（≥1MB 且 zip 魔数），无效删除文件继续下一候选
        var candidates = new List<string> { serverUrl };
        if (serverUrl.Contains("piston-data.mojang.com"))
            candidates.Add(serverUrl.Replace("piston-data.mojang.com", "launcher.mojang.com"));
        candidates.Add($"https://bmclapi2.bangbang93.com/version/{Uri.EscapeDataString(versionId)}/server");
        Exception? last = null;
        foreach (var url in candidates)
        {
            try
            {
                await _downloads.DownloadFileAsync(url, jarPath, null, expectedSize, progress, ct);
                if (IsValidServerJar(jarPath))
                {
                    last = null;
                    break;
                }
                var len = new FileInfo(jarPath).Length;
                File.Delete(jarPath);
                last = new InvalidDataException($"「{url}」返回内容无效（{len} 字节，非服务端 jar）");
            }
            catch (Exception ex) { last = ex; }
        }
        if (last is not null)
        {
            // 汇总失败信息（AL4）：用户一眼看到各源失败原因，不再只有一个笼统错误
            throw new InvalidOperationException(
                $"服务端下载失败（已尝试 {candidates.Count} 个源，最后错误：{last.Message}）", last);
        }
        WriteDefaultProperties(dir);
        return jarPath;
    }

    /// <summary>服务端 jar 有效性：≥1MB 且 zip 魔数（PK）——拦 200 错误页/挑战页</summary>
    private static bool IsValidServerJar(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < 1024 * 1024) return false;
            using var fs = File.OpenRead(path);
            return fs.ReadByte() == 0x50 && fs.ReadByte() == 0x4B;
        }
        catch { return false; }
    }

    /// <summary>
    /// 预写默认 server.properties（AH1：新服默认离线模式）。文件不存在才写——
    /// Minecraft 服务端首次启动发现文件已存在则直接使用，不覆盖；已有配置的服务端不受影响。
    /// 键与开服页 PropDefs 对齐（ServerProperties.Load 可解析）。
    /// </summary>
    public static void WriteDefaultProperties(string serverDir)
    {
        var path = Path.Combine(serverDir, "server.properties");
        if (File.Exists(path)) return;
        Directory.CreateDirectory(serverDir);
        File.WriteAllLines(path, [
            "#Generated by YanKa Launcher — 新服默认配置（可在开服页图形化修改）",
            "online-mode=false",
            "server-port=25565",
            "max-players=20",
            "motd=YanKa Launcher Server",
            "view-distance=10",
            "difficulty=normal",
            "gamemode=survival",
            "pvp=true",
            "white-list=false",
            "level-name=world",
        ]);
    }

    /// <summary>加载父版本 json（inheritsFrom 链解析用）；缺失/损坏返回 null</summary>
    private static VersionJson? LoadParent(string gameDir, string id)
    {
        var p = Path.Combine(gameDir, "versions", id, $"{id}.json");
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(p)); }
        catch { return null; }
    }

    /// <summary>同意 EULA（写入 eula.txt）</summary>
    public static void AcceptEula(string serverDir)
    {
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(Path.Combine(serverDir, "eula.txt"), "eula=true");
    }
}
