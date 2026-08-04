using System.Text.Json;

namespace Launcher.Core.Server;

/// <summary>封禁条目（banned-players.json 一行：名字 + 封禁时间）</summary>
public sealed record ServerBannedEntry(string Name, string Created, string Expires);

/// <summary>
/// banned-players.json 解析（AL2 批次：启动器图形化管理封禁——ban 后不在线的玩家也能解封）。
/// 文件由 Minecraft 服务端维护（ban/pardon 命令写入），启动器只读展示。
/// </summary>
public static class ServerBannedFile
{
    /// <summary>读 banned-players.json → 封禁列表（按名字排序）；文件缺失/损坏返回空列表</summary>
    public static IReadOnlyList<ServerBannedEntry> Load(string serverDir)
    {
        var path = Path.Combine(serverDir, "banned-players.json");
        if (!File.Exists(path)) return [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var list = new List<ServerBannedEntry>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;
                var created = el.TryGetProperty("created", out var c) ? c.GetString() ?? "" : "";
                var expires = el.TryGetProperty("expires", out var e) ? e.GetString() ?? "" : "";
                list.Add(new ServerBannedEntry(name, created, expires));
            }
            return [.. list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch { return []; }
    }
}
