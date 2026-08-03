using System.Text.Json;

namespace Launcher.Core.Utils;

/// <summary>
/// 启动器设置（AppData\Launcher\settings.json）：自配游戏路径 + 版本隔离开关。
/// </summary>
public enum DensityMode { Compact = 0, Normal = 1, Comfortable = 2 }

/// <summary>下载并发档位（分片连接数：低 8 / 中 16 / 高 24）</summary>
public enum DownloadTier { Low = 8, Medium = 16, High = 24 }

public sealed class LauncherSettings
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "settings.json");

    /// <summary>自配游戏目录（如 C:\Users\yanka\Downloads\YanKa Launcher\.minecraft）；null = 自动探测</summary>
    public string? GameDirectory { get; set; }

    /// <summary>版本隔离（每个版本独立 saves/mods/options.txt，不串门）</summary>
    public bool VersionIsolation { get; set; } = true;

    // ---------- 启动 ----------

    /// <summary>游戏内存上限（MB）；0 = 自动（总内存 60%）</summary>
    public int MemoryMb { get; set; } = 4096;

    /// <summary>Java 路径；null = 自动选配（PCL runtime / PATH）</summary>
    public string? JavaPath { get; set; }

    /// <summary>额外 JVM 参数（空格分隔，如 "-Dxxx=1 -Xss2m"）；null = 无</summary>
    public string? ExtraJvmArgs { get; set; }

    /// <summary>启动时自动写入中文语言（options.txt lang:zh_cn）</summary>
    public bool AutoChineseEnabled { get; set; } = true;

    // ---------- 下载 ----------

    /// <summary>下载失败时回退镜像源（BMCLAPI 等）</summary>
    public bool MirrorFallbackEnabled { get; set; } = true;

    /// <summary>最大并发下载数（0 = 默认）</summary>
    public int MaxConcurrentDownloads { get; set; }

    /// <summary>下载限速（KB/s；0 = 不限速）</summary>
    public int DownloadSpeedLimitKbps { get; set; }

    /// <summary>下载并发档位（分片数：低 8 / 中 16 / 高 24）</summary>
    public DownloadTier DownloadTier { get; set; } = DownloadTier.Low;

    /// <summary>分片连接数覆盖（0 = 用档位默认）</summary>
    public int ChunkCount { get; set; }

    /// <summary>分片缓冲区覆盖（字节；0 = 默认 81920）</summary>
    public int BufferSize { get; set; }

    /// <summary>CurseForge API Key（空 = 禁用 CF 源）</summary>
    public string CurseForgeApiKey { get; set; } = "";

    // ---------- 外观 ----------

    /// <summary>窗口透明度（0.7-1.0；1.0 = 不透明）</summary>
    public double WindowOpacity { get; set; } = 0.9;

    /// <summary>强调色（#RRGGBB；空 = 默认青绿）</summary>
    public string AccentColor { get; set; } = "#2DD4BF";

    /// <summary>界面密度（紧凑/标准/舒适 → 整 UI 缩放）</summary>
    public DensityMode Density { get; set; } = DensityMode.Normal;

    /// <summary>窗口宽度（0 = 未设置，用默认 860）</summary>
    public double WindowWidth { get; set; }

    /// <summary>窗口高度（0 = 未设置，用默认 560）</summary>
    public double WindowHeight { get; set; }

    public static LauncherSettings Current { get; } = Load();

    public static LauncherSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path));
                if (s is not null) return s;
            }
        }
        catch { /* 坏 JSON 回退默认 */ }
        return new LauncherSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失败不阻塞 */ }
    }
}
