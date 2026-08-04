using System.Text.Json;

namespace Launcher.Core.Server;

/// <summary>OP 条目（ops.json 一行：名字 + 权限等级）</summary>
public sealed record ServerOpEntry(string Name, int Level);

/// <summary>
/// ops.json 解析（AI 批次：启动器图形化管理服务器权限，不依赖游戏内命令）。
/// 文件由 Minecraft 服务端维护（op/deop 命令写入），启动器只读展示。
/// </summary>
public static class ServerOpsFile
{
    /// <summary>读 ops.json → OP 列表（按名字排序）；文件缺失/损坏返回空列表</summary>
    public static IReadOnlyList<ServerOpEntry> Load(string serverDir)
    {
        var path = Path.Combine(serverDir, "ops.json");
        if (!File.Exists(path)) return [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var list = new List<ServerOpEntry>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;
                var level = el.TryGetProperty("level", out var l) && l.TryGetInt32(out var iv) ? iv : 0;
                list.Add(new ServerOpEntry(name, level));
            }
            return [.. list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch { return []; }
    }
}
