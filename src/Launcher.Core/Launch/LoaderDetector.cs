using System.Text.Json;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Launch;

/// <summary>
/// 真实加载器检测：读版本 json（解析 inheritsFrom 链）按 mainClass 判定 fabric/forge/neoforge/quilt；
/// 纯原版返回 ""。替代"从版本名猜"（LoaderBadgeOf/GuessLoader）——实例下拉与 mod 下载筛选用真实值，
/// 原版实例不再误配到加载器文件。
/// </summary>
public static class LoaderDetector
{
    /// <summary>返回 "fabric"/"forge"/"neoforge"/"quilt"；纯原版返回 ""；json 缺失返回 null（调用方兜底）</summary>
    public static string? Detect(string gameDir, string versionId)
    {
        var json = LoadMerged(gameDir, versionId);
        if (json is null) return null;
        var main = json.MainClass ?? "";
        if (main.Contains("net.fabricmc.loader", StringComparison.OrdinalIgnoreCase)) return "fabric";
        if (main.Contains("cpw.mods.bootstraplauncher", StringComparison.OrdinalIgnoreCase)) return "neoforge";
        if (main.Contains("cpw.mods.modlauncher", StringComparison.OrdinalIgnoreCase)) return "forge";
        if (main.Contains("org.quiltmc.loader", StringComparison.OrdinalIgnoreCase)) return "quilt";
        return ""; // 纯原版（net.minecraft.client.main.Main）
    }

    /// <summary>读版本 json（解析 inheritsFrom 链——Fabric/Forge profile 的 mainClass 在自身 json）；缺失返回 null</summary>
    private static VersionJson? LoadMerged(string gameDir, string versionId)
    {
        try
        {
            var p = Path.Combine(gameDir, "versions", versionId, $"{versionId}.json");
            if (!File.Exists(p)) return null;
            var v = JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(p));
            if (v is null) return null;
            return VersionJsonMerger.ResolveChain(v, id => LoadParent(gameDir, id));
        }
        catch { return null; }
    }

    private static VersionJson? LoadParent(string gameDir, string id)
    {
        try
        {
            var p = Path.Combine(gameDir, "versions", id, $"{id}.json");
            if (!File.Exists(p)) return null;
            return JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(p));
        }
        catch { return null; }
    }
}
