using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Launcher.Core.Launch;

/// <summary>
/// 8-13 离线皮肤资源包（PCL 式，不侵入版本文件）：把皮肤 PNG 打成资源包
/// resourcepacks/LatticeSkin.zip（替换 Steve/Alex 默认纹理——游戏内离线皮肤生效），
/// 并在 options.txt 的 resourcePacks 列表注入该包（游戏启动自动加载）。
/// 适用于 littleskin 登录皮肤同步 + 本地换肤（此前游戏内不生效——Minecraft 限制的解法）。
/// </summary>
public static class SkinPack
{
    /// <summary>资源包文件名（options.txt 引用名）</summary>
    public const string PackFileName = "LatticeSkin.zip";

    /// <summary>
    /// 同步资源包 + options.txt 注入。gameDir = 启动目标目录（版本隔离时是 versions/{id}）。
    /// packFormat 按 MC 版本映射（未知版本保守 15——游戏提示「旧版制作」但仍加载）。
    /// </summary>
    public static void Apply(string gameDir, string skinPngPath, int packFormat = 15)
    {
        try
        {
            if (!File.Exists(skinPngPath)) return;
            var packsDir = Path.Combine(gameDir, "resourcepacks");
            Directory.CreateDirectory(packsDir);
            var packPath = Path.Combine(packsDir, PackFileName);
            WritePack(packPath, skinPngPath, packFormat);
            InjectOptions(Path.Combine(gameDir, "options.txt"));
        }
        catch { /* 皮肤包失败不阻塞启动 */ }
    }

    /// <summary>生成资源包 zip：pack.mcmeta + steve.png + alex.png（同一皮肤纹理替换两个默认模型）</summary>
    private static void WritePack(string packPath, string skinPngPath, int packFormat)
    {
        var mcmeta = JsonSerializer.Serialize(new
        {
            pack = new { pack_format = packFormat, description = "Lattice Skin" },
        });
        var skinBytes = File.ReadAllBytes(skinPngPath);
        using var fs = new FileStream(packPath, FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        WriteEntry(zip, "pack.mcmeta", Encoding.UTF8.GetBytes(mcmeta));
        WriteEntry(zip, "assets/minecraft/textures/entity/steve.png", skinBytes);
        WriteEntry(zip, "assets/minecraft/textures/entity/alex.png", skinBytes);
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    /// <summary>options.txt 注入：resourcePacks:[...] 行加入 "LatticeSkin.zip"（已存在保序跳过；无该行追加）</summary>
    private static void InjectOptions(string optionsPath)
    {
        var lines = File.Exists(optionsPath)
            ? File.ReadAllLines(optionsPath).ToList()
            : [];

        var idx = lines.FindIndex(l => l.StartsWith("resourcePacks:"));
        if (idx >= 0)
        {
            var line = lines[idx];
            if (!line.Contains($"\"{PackFileName}\""))
            {
                var colon = line.IndexOf(':');
                var prefix = colon >= 0 ? line[..(colon + 1)] : "resourcePacks:";
                var rest = colon >= 0 ? line[(colon + 1)..] : "[]";
                lines[idx] = prefix + MergeArray(rest, PackFileName);
            }
        }
        else
        {
            lines.Add($"resourcePacks:[\"{PackFileName}\"]");
        }

        File.WriteAllLines(optionsPath, lines);
    }

    /// <summary>"[a,b]" 尾部插入新项（保持 JSON 合法；空/非法回退覆盖为单元素数组）</summary>
    internal static string MergeArray(string arrayText, string item)
    {
        var trimmed = arrayText.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            var inner = trimmed[1..^1].Trim();
            var quoted = $"\"{item}\"";
            return inner.Length == 0 ? $"[{quoted}]" : $"[{inner},{quoted}]";
        }
        return $"[\"{item}\"]"; // 非法格式：覆盖（保启动器行为确定）
    }

    /// <summary>8-13 皮肤图片尺寸校验：Minecraft 皮肤只认 64×64（新格式）或 64×32（旧格式）。
    /// 其他尺寸（图标/截图/高清皮肤）游戏内显示会错乱——拒绝并提示。</summary>
    public static bool IsSupportedSize(int width, int height)
        => width == 64 && (height == 64 || height == 32);

    /// <summary>MC 版本 → pack_format（取保守值：新版游戏对旧格式只提示「为旧版本制作」但仍加载；
    /// 反过来的高格式低版本会拒载，所以宁低勿高。未知版本 15）</summary>
    public static int PackFormatFor(string versionId)
    {
        var v = versionId.ToLowerInvariant();
        if (v.StartsWith("1.21") || v.StartsWith("1.22") || v.StartsWith("1.23")
            || v.StartsWith("1.24") || v.StartsWith("1.25") || v.StartsWith("26."))
            return 34; // 1.21+ 通用
        if (v.StartsWith("1.20.5") || v.StartsWith("1.20.6")) return 32;
        if (v.StartsWith("1.20")) return 15;
        if (v.StartsWith("1.19")) return 13;
        if (v.StartsWith("1.18")) return 8;
        if (v.StartsWith("1.17")) return 7;
        return 15;
    }
}
