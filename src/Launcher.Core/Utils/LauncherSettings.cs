using System.Text.Json;

namespace Launcher.Core.Utils;

/// <summary>
/// 启动器设置（AppData\Launcher\settings.json）：自配游戏路径 + 版本隔离开关。
/// </summary>
public sealed class LauncherSettings
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "settings.json");

    /// <summary>自配游戏目录（如 C:\Users\yanka\Downloads\YanKa Launcher\.minecraft）；null = 自动探测</summary>
    public string? GameDirectory { get; set; }

    /// <summary>版本隔离（每个版本独立 saves/mods/options.txt，不串门）</summary>
    public bool VersionIsolation { get; set; } = true;

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
