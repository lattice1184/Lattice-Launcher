using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Launcher.Core.Multiplayer;

/// <summary>
/// EasyTier 联机模块的下载 / 校验 / 安装（锁版本 v2.6.4，SHA256 必校验；LGPL-3.0 进程外调用）。
/// 候选链：GitHub 直连 → 镜像（ghfast.top / ghproxy.net——镜像域名易变，任一可用即过）。
/// 安装到 %AppData%\Launcher\tools\easytier\{version}\。模式对齐 TerracottaProvisioningService。
/// </summary>
public sealed class EasyTierProvisioningService
{
    /// <summary>锁定的 EasyTier 版本</summary>
    public const string LockedVersion = "2.6.4";

    /// <summary>已知 SHA256（{version}/x86_64）——资产校验用（8-14 实测下载 32.6MB 后计算写入）</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownDigests = new Dictionary<string, string>
    {
        ["2.6.4/x86_64"] = "27af91e270e554709b048bd32327fefd2dfce5062ae1e8701af7550c6f525f84",
    };

    public static string AssetName(string version) => $"easytier-windows-x86_64-v{version}.zip";

    /// <summary>GitHub 资产 URL（官方源）</summary>
    public static string GitHubAssetUrl(string version)
        => $"https://github.com/EasyTier/EasyTier/releases/download/v{version}/{AssetName(version)}";

    /// <summary>镜像候选（国内加速；域名易变——候选链依序尝试）</summary>
    public static string[] MirrorUrls(string version) =>
    [
        $"https://ghfast.top/https://github.com/EasyTier/EasyTier/releases/download/v{version}/{AssetName(version)}",
        $"https://ghproxy.net/https://github.com/EasyTier/EasyTier/releases/download/v{version}/{AssetName(version)}",
    ];

    /// <summary>安装根：%AppData%\Launcher\tools\easytier</summary>
    public static string ModuleRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "tools", "easytier");

    private const long MaxArchiveBytes = 128 * 1024 * 1024;
    private const int BufferSize = 81920;

    private static readonly SemaphoreSlim InstallLock = new(1, 1);

    private readonly HttpClient _http;

    public EasyTierProvisioningService(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("YanKa-Launcher/1.0");
    }

    /// <summary>已装模块（easytier-core.exe + easytier-cli.exe 都在则可用），无则 null</summary>
    public bool TryGetAvailable(out string moduleDir)
    {
        moduleDir = Path.Combine(ModuleRoot, LockedVersion);
        return File.Exists(Path.Combine(moduleDir, "easytier-core.exe"))
            && File.Exists(Path.Combine(moduleDir, "easytier-cli.exe"));
    }

    /// <summary>重装（一键修复）：清版本目录 → 重新下载安装</summary>
    public async Task<string> ReinstallAsync(CancellationToken ct = default)
    {
        var dir = Path.Combine(ModuleRoot, LockedVersion);
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
        return await EnsureAvailableAsync(ct);
    }

    /// <summary>确保模块可用：已装直接返回；否则下载安装（校验 SHA256 + 解压）。并发串行。</summary>
    public async Task<string> EnsureAvailableAsync(CancellationToken ct = default)
    {
        await InstallLock.WaitAsync(ct);
        try
        {
            if (TryGetAvailable(out var installed)) return installed;

            var version = LockedVersion;
            var expectedSha = KnownDigests.TryGetValue($"{version}/x86_64", out var s) && s != "pending" ? s : null;

            var candidates = new List<string> { GitHubAssetUrl(version) };
            candidates.AddRange(MirrorUrls(version));
            string? lastError = null;
            foreach (var url in candidates)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await DownloadAndInstallAsync(version, url, expectedSha, ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }
            throw new MultiplayerLobbyException(
                MultiplayerLobbyFailure.BackendUnavailable,
                $"EasyTier 模块下载失败：{lastError ?? "未知错误"}（已尝试官方源与镜像）");
        }
        finally
        {
            InstallLock.Release();
        }
    }

    private async Task<string> DownloadAndInstallAsync(string version, string url, string? expectedSha, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"easytier-{Guid.NewGuid():N}.zip");
        try
        {
            // 流式落盘 + SHA256 边下边算（大文件不占内存）
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            await using (var fs = File.Create(temp))
            {
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                var sha = SHA256.Create();
                var buffer = new byte[BufferSize];
                long read = 0;
                while (true)
                {
                    var n = await src.ReadAsync(buffer, ct);
                    if (n == 0) break;
                    sha.TransformBlock(buffer, 0, n, null, 0);
                    read += n;
                    if (read > MaxArchiveBytes)
                        throw new InvalidDataException($"EasyTier 包超限（>{MaxArchiveBytes / 1024 / 1024}MB）");
                    fs.Write(buffer, 0, n);
                }
                sha.TransformFinalBlock([], 0, 0);
                if (expectedSha is not null)
                {
                    var actual = Convert.ToHexString(sha.Hash!);
                    if (!string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"EasyTier 包校验失败（期望 {expectedSha[..8]}… 实际 {actual[..8]}…）");
                }
                MultiplayerLog.Log($"EasyTier 下载完成：{read / 1024 / 1024}MB（sha {expectedSha?[..8] ?? "未校验"}）");
            }

            // 解压到版本目录（zip 内两个 exe：core + cli）
            var moduleDir = Path.Combine(ModuleRoot, version);
            Directory.CreateDirectory(moduleDir);
            ZipFile.ExtractToDirectory(temp, moduleDir, overwriteFiles: true);
            if (!TryGetAvailable(out _))
                throw new InvalidDataException("EasyTier 包内容不完整（缺 easytier-core.exe / easytier-cli.exe）");
            return moduleDir;
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }
}
