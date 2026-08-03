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

    /// <summary>服务端目录（servers/{versionId}）</summary>
    public static string ServerDir(string gameDir, string versionId)
        => Path.Combine(gameDir, "servers", versionId);

    /// <summary>从已装版本安装服务端 → server.jar 路径（幂等：已存在且大小正确则跳过）</summary>
    public async Task<string> InstallAsync(string versionId, string gameDir,
        DownloadProgressHandler? progress = null, CancellationToken ct = default)
    {
        var versionPath = Path.Combine(gameDir, "versions", versionId, $"{versionId}.json");
        if (!File.Exists(versionPath))
            throw new FileNotFoundException($"版本 {versionId} 未安装（请先在版本页下载）");

        var version = JsonSerializer.Deserialize<VersionJson>(await File.ReadAllTextAsync(versionPath, ct))
            ?? throw new InvalidDataException($"版本 JSON 解析失败: {versionId}");
        var serverUrl = version.Downloads?.Server?.Url;
        if (string.IsNullOrEmpty(serverUrl))
            throw new InvalidDataException($"版本 {versionId} 没有服务端下载链接（不支持开服）");
        var size = version.Downloads!.Server!.Size ?? 0;

        var dir = ServerDir(gameDir, versionId);
        Directory.CreateDirectory(dir);
        var jarPath = Path.Combine(dir, "server.jar");
        await _downloads.DownloadFileAsync(serverUrl, jarPath, null, size, progress, ct);
        return jarPath;
    }

    /// <summary>同意 EULA（写入 eula.txt）</summary>
    public static void AcceptEula(string serverDir)
    {
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(Path.Combine(serverDir, "eula.txt"), "eula=true");
    }
}
