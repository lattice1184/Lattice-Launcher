using System.Text.Json;

namespace Launcher.Core.Utils;

/// <summary>
/// 版本级启动配置（versions/{id}/.yanla-config.json）：内存/Java/额外参数，
/// 字段为 null 时跟随全局设置（LauncherSettings）。PCL2 式"版本设置"。
/// </summary>
public sealed class VersionConfig
{
    /// <summary>版本级内存（MB）；null = 跟随全局</summary>
    public int? MemoryMb { get; set; }

    /// <summary>版本级 Java 路径；null = 跟随全局（自动）</summary>
    public string? JavaPath { get; set; }

    /// <summary>版本级额外 JVM 参数；null = 跟随全局</summary>
    public string? ExtraJvmArgs { get; set; }

    /// <summary>是否设置了任何版本级覆盖</summary>
    public bool HasOverrides => MemoryMb is not null || JavaPath is not null || ExtraJvmArgs is not null;
}

public static class VersionConfigService
{
    /// <summary>读取版本级配置（文件缺失/坏 JSON → 默认全 null）</summary>
    public static VersionConfig Load(string gameDir, string versionId)
    {
        var path = ConfigPath(gameDir, versionId);
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<VersionConfig>(File.ReadAllText(path));
                if (cfg is not null) return cfg;
            }
        }
        catch { /* 坏数据回退默认 */ }
        return new VersionConfig();
    }

    public static void Save(string gameDir, string versionId, VersionConfig config)
    {
        var path = ConfigPath(gameDir, versionId);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失败不阻塞 */ }
    }

    /// <summary>清除版本级覆盖（全部跟随全局）</summary>
    public static void Reset(string gameDir, string versionId)
    {
        try { File.Delete(ConfigPath(gameDir, versionId)); } catch { }
    }

    /// <summary>合并：版本级非 null 字段覆盖全局（返回最终生效配置）</summary>
    public static (int MemoryMb, string? JavaPath, string? ExtraJvmArgs) Merge(
        string gameDir, string versionId, LauncherSettings global)
    {
        var cfg = Load(gameDir, versionId);
        var mem = cfg.MemoryMb ?? global.MemoryMb;
        var java = cfg.JavaPath ?? global.JavaPath;
        var args = cfg.ExtraJvmArgs ?? global.ExtraJvmArgs;
        return (mem, java, args);
    }

    private static string ConfigPath(string gameDir, string versionId)
        => Path.Combine(gameDir, "versions", versionId, ".yanla-config.json");
}
