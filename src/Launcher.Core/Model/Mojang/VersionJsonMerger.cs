namespace Launcher.Core.Model.Mojang;

/// <summary>
/// 加载器版本 JSON 合并：inheritsFrom 链解析（Forge/NeoForge 安装器与部分 Fabric 生成的
/// version.json 继承原版）。规则：
/// - mainClass/assetIndex/javaVersion/logging/minecraftArguments/arguments/downloads 子优先（arguments 整体替换，不拼接）；
/// - libraries = 父 + 子按 Maven 名去重（子覆盖）；
/// - downloads 子缺时继承父（供下载编排把 client jar 落到子版本目录）；
/// - id 永远取子。
/// </summary>
public static class VersionJsonMerger
{
    private const int MaxChainDepth = 8;

    public static VersionJson Merge(VersionJson child, VersionJson parent) => child with
    {
        MainClass = child.MainClass ?? parent.MainClass,
        Assets = child.Assets ?? parent.Assets,
        AssetIndex = child.AssetIndex ?? parent.AssetIndex,
        JavaVersion = child.JavaVersion ?? parent.JavaVersion,
        Logging = child.Logging ?? parent.Logging,
        MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,
        Arguments = child.Arguments ?? parent.Arguments,
        Downloads = child.Downloads ?? parent.Downloads,
        Libraries = MergeLibraries(parent.Libraries, child.Libraries),
    };

    /// <summary>父库在前、子库在后；同名（Maven 坐标）子覆盖父</summary>
    private static List<LibraryJson>? MergeLibraries(List<LibraryJson>? parent, List<LibraryJson>? child)
    {
        var merged = new List<LibraryJson>();
        if (parent is not null) merged.AddRange(parent);
        if (child is null) return merged;
        foreach (var lib in child)
        {
            var existing = merged.FindIndex(m => m.Name == lib.Name);
            if (existing >= 0) merged[existing] = lib;
            else merged.Add(lib);
        }
        return merged;
    }

    /// <summary>
    /// 逐层加载父版本 JSON 合并；环检测 + 深度上限；父缺失 = 链终止。
    /// 全链解析成功时清空 InheritsFrom；否则保留（调用方据此报"父版本未安装"）。
    /// </summary>
    public static VersionJson ResolveChain(VersionJson leaf, Func<string, VersionJson?> loadById)
    {
        var current = leaf;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { leaf.Id };
        var pending = leaf.InheritsFrom;
        var complete = true;
        for (var depth = 0; depth < MaxChainDepth && pending is { } parentId; depth++)
        {
            if (!seen.Add(parentId)) { complete = false; break; }   // 环
            var parent = loadById(parentId);
            if (parent is null) { complete = false; break; }        // 父缺失
            current = Merge(current, parent);
            pending = parent.InheritsFrom;
        }
        return complete ? current with { InheritsFrom = null } : current;
    }
}
