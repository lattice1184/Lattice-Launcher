using System.Text.Json;
using Launcher.Core.Launch;

namespace Launcher.App.Services;

/// <summary>
/// 已装版本扫描共享工具（主页 / 版本页 / 开服页三处统一）：
/// 读版本 json 返回加载器徽章 + 继承的原版版本；被加载器继承的原版条目从列表隐藏（依赖不单独显示）。
/// </summary>
public static class VersionScan
{
    /// <summary>读版本信息（读 json 一次）：Loader = 加载器徽章（真实检测 → 名字兜底），McVersion = 继承的原版版本（加载器版本）</summary>
    public static (string Loader, string McVersion) Inspect(string gameDir, string id)
    {
        var loader = LoaderDetector.Detect(gameDir, id);
        if (loader is null)
        {
            var lower = id.ToLowerInvariant();
            foreach (var kw in new[] { "neoforge", "fabric", "forge", "quilt" })
                if (lower.Contains(kw)) { loader = kw; break; }
        }
        loader ??= "";
        var mc = "";
        try
        {
            var json = Path.Combine(gameDir, "versions", id, $"{id}.json");
            if (File.Exists(json))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(json));
                if (doc.RootElement.TryGetProperty("inheritsFrom", out var p) && p.GetString() is { } pid)
                    mc = pid;
            }
        }
        catch { /* json 缺失/损坏：无继承信息 */ }
        return (loader, mc);
    }

    /// <summary>PCL 式显示名：加载器版本 → "1.21.11 (Fabric)"，原版保持原名（主页/版本页统一，勿各写一份）</summary>
    public static string FriendlyName(string id, string loader, string mcVersion)
        => loader.Length > 0 && mcVersion.Length > 0
            ? $"{mcVersion} ({Cap(loader)})"
            : id;

    /// <summary>
    /// 有效 client jar 判定（与两种下载语义对齐，版本页行徽章/详情共用）：
    /// ① 自身目录 {id}.jar（原版单独下载/补全后）；② 父版本目录 {parent}.jar（官方 Forge 安装器落父目录）；
    /// ③ 引用我的已装子版本目录有 jar（Lattice 下载 H6 落子目录——原版条目目录无 jar 但游戏能跑，非缺失）。
    /// children = 已装父版本 id → (子 id, 子目录)。任一满足 = 不缺失；否则才是真残件（json-only）。
    /// </summary>
    public static bool HasUsableClientJar(string gameDir, string id, string? parent,
        IReadOnlyDictionary<string, List<(string ChildId, string ChildDir)>> childrenByParent)
    {
        if (File.Exists(Path.Combine(gameDir, "versions", id, $"{id}.jar"))) return true;
        if (!string.IsNullOrEmpty(parent)
            && File.Exists(Path.Combine(gameDir, "versions", parent, $"{parent}.jar")))
            return true;
        if (childrenByParent.TryGetValue(id, out var kids)
            && kids.Any(k => File.Exists(Path.Combine(k.ChildDir, "versions", k.ChildId, $"{k.ChildId}.jar"))))
            return true;
        return false;
    }

    private static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
}
