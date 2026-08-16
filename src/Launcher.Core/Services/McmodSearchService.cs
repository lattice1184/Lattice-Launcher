using System.Text;
using System.Text.RegularExpressions;
using Launcher.Core.Download;

namespace Launcher.Core.Services;

/// <summary>
/// MC百科（mcmod.cn）中文搜索（AL63）：Modrinth 搜索索引为英文标题——中文查询无结果
/// （实测「遗落荒野」Modrinth 原生 0 命中）。链路：
///   中文 → search.mcmod.cn 搜索结果页（HTML 静态，正则解析条目 id + 中文标题）
///   → 条目详情页（www.mcmod.cn/class/{id}.html）→ link.mcmod.cn/target/{base64(完整 URL)}
///   双层编码解出 Modrinth 链接 → slug → Modrinth API 拿项目。
/// 两个页面均为静态 HTML，正则解析（无第三方依赖）；mcmod 国内直连可达（实测 200/0.4s）。
/// </summary>
public sealed class McmodSearchService
{
    private static readonly HttpClient Http = HttpClientPool.Create();

    /// <summary>搜索结果条目：&lt;a target="_blank" href="https://www.mcmod.cn/class/{id}.html"&gt;{中文标题}&lt;/a&gt;</summary>
    private static readonly Regex EntryRegex = new(
        @"href=""https://www\.mcmod\.cn/class/(\d+)\.html""[^>]*>([^<]{1,60})<",
        RegexOptions.Compiled);

    /// <summary>详情页 Modrinth 外链：data-original-title="Modrinth" ... href="//link.mcmod.cn/target/{base64}"</summary>
    private static readonly Regex ModrinthLinkRegex = new(
        @"data-original-title=""Modrinth""[^>]*?href=""//link\.mcmod\.cn/target/([A-Za-z0-9+/=]+)""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>解析搜索结果页 → (条目 id, 中文标题) 列表</summary>
    public static List<(string ClassId, string Title)> ParseSearchResults(string html)
    {
        var list = new List<(string, string)>();
        foreach (Match m in EntryRegex.Matches(html))
            list.Add((m.Groups[1].Value, m.Groups[2].Value.Trim()));
        return list;
    }

    /// <summary>解析详情页 → Modrinth slug（无 Modrinth 外链返回 null）</summary>
    public static string? DecodeModrinthSlug(string detailHtml)
    {
        var m = ModrinthLinkRegex.Match(detailHtml);
        if (!m.Success) return null;
        try
        {
            var url = Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value));
            var idx = url.IndexOf("/mod/", StringComparison.Ordinal);
            return idx < 0 ? null : url[(idx + 5)..];
        }
        catch { return null; }
    }

    /// <summary>中文查询 → (Modrinth slug, 中文标题) 列表（去重，上限 maxResults；失败/无外链条目跳过）</summary>
    public async Task<List<(string Slug, string ChineseTitle)>> SearchSlugsAsync(
        string query, int maxResults, CancellationToken ct)
    {
        var searchUrl = $"https://search.mcmod.cn/s?key={Uri.EscapeDataString(query)}";
        string html;
        try { html = await Http.GetStringAsync(searchUrl, ct); }
        catch { return []; }

        var slugs = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = ParseSearchResults(html).Take(maxResults).ToList();
        // 8-22 详情页并行解析（旧串行 10 条目 × 0.4-2s = 10s+ 干等，观感像死掉）；门 4 防打爆 mcmod
        using var gate = new SemaphoreSlim(4);
        var tasks = entries.Select(async entry =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var detail = await Http.GetStringAsync($"https://www.mcmod.cn/class/{entry.ClassId}.html", ct);
                return (Slug: DecodeModrinthSlug(detail), entry.Title);
            }
            catch { return (Slug: (string?)null, entry.Title); }
            finally { gate.Release(); }
        }).ToArray();
        foreach (var t in tasks)
        {
            var (slug, title) = await t;
            if (slug is not null && seen.Add(slug))
                slugs.Add((slug, title));
        }
        return slugs;
    }

    /// <summary>查询是否含中文（CJK）——中文搜索链路触发条件</summary>
    public static bool ContainsChinese(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        return query.Any(c => (uint)c is >= 0x4E00 and <= 0x9FFF);
    }
}
