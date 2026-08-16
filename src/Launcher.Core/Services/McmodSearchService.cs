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

    /// <summary>搜索结果条目：&lt;a target="_blank" href="https://www.mcmod.cn/class/{id}.html"&gt;{中文标题，可能含 &lt;em&gt; 高亮}&lt;/a&gt;。
    /// 8-22 修复：旧正则要求标题首个字符非 &lt;（`[^&lt;]{1,60}`）——MC百科对命中词用 &lt;em&gt; 包裹，
    /// 搜「钠」时 Sodium 本体标题是 `&lt;em&gt;钠&lt;/em&gt; (Sodium)` → 首字符是 &lt; → 整条被跳过
    /// （真机：钠本体永远不出现在结果里）。改为捕获到 &lt;/a&gt; 前再剥标签。</summary>
    private static readonly Regex EntryRegex = new(
        @"href=""https://www\.mcmod\.cn/class/(\d+)\.html""[^>]*>(.{1,120}?)</a>",
        RegexOptions.Compiled);

    /// <summary>剥 HTML 标签（&lt;em&gt; 高亮等）</summary>
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>详情页 Modrinth 外链：data-original-title="Modrinth" ... href="//link.mcmod.cn/target/{base64}"</summary>
    private static readonly Regex ModrinthLinkRegex = new(
        @"data-original-title=""Modrinth""[^>]*?href=""//link\.mcmod\.cn/target/([A-Za-z0-9+/=]+)""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>解析搜索结果页 → (条目 id, 中文标题) 列表</summary>
    public static List<(string ClassId, string Title)> ParseSearchResults(string html)
    {
        var list = new List<(string, string)>();
        foreach (Match m in EntryRegex.Matches(html))
        {
            var title = HtmlTagRegex.Replace(m.Groups[2].Value, "").Trim();
            if (title.Length == 0) continue;
            list.Add((m.Groups[1].Value, title));
        }
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

/// <summary>
/// 常见模组中英别名表（8-22，PCL 式精准搜索）：中文查询命中内置映射 → 直接查 Modrinth slug
/// （缓存秒回）——「钠」直接出 Sodium 本体，不依赖 MC百科解析（<em> 高亮/无外链都绕开了）。
/// 只收录 Modrinth 上存在的项目（OptiFine 等无 Modrinth 的不收——避免 404）。
/// </summary>
public static class ModAliasTable
{
    /// <summary>中文名 → Modrinth slug（多义时多条：如「小地图」→ Xaero 两个）</summary>
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["钠"] = ["sodium"],
        ["钠扩展"] = ["sodium-extra"],
        ["虹吸"] = ["iris"],
        ["简单语音"] = ["simple-voice-chat"],
        ["旅行地图"] = ["journeymap"],
        ["小地图"] = ["xaeros-minimap", "xaeros-world-map"],
        ["世界地图"] = ["xaeros-world-map"],
        ["苹果皮"] = ["appleskin"],
        ["动态fps"] = ["dynamic-fps"],
        ["帕秋莉"] = ["patchouli"],
        ["玉"] = ["jade"],
        ["连锁采集"] = ["vein-miner"],
        ["一键整理"] = ["inventory-sorter"],
        ["鼠标手势"] = ["mouse-tweaks"],
        ["铁氧体"] = ["ferrite-core"],
        ["锂"] = ["lithium"],
        ["磷"] = ["phosphor"],
        ["懒加载语言"] = ["lazy-language-loader"],
        ["模组菜单"] = ["modmenu"],
        ["布匹配置"] = ["cloth-config"],
    };

    /// <summary>中文 query → 命中的别名 slug 列表（最长匹配优先；无命中空）。「钠扩展」命中扩展不命中「钠」。</summary>
    public static IReadOnlyList<string> Resolve(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var best = "";
        var result = new List<string>();
        foreach (var (key, slugs) in Map)
        {
            if (query.Contains(key, StringComparison.OrdinalIgnoreCase) && key.Length > best.Length)
            {
                best = key;
                result = [.. slugs];
            }
        }
        return result;
    }

    /// <summary>命中时显示的中文标题（取命中的键——「钠」→「钠 (Sodium)」）</summary>
    public static string TitleFor(string? query, string slug)
    {
        foreach (var (key, slugs) in Map)
            if (slugs.Contains(slug, StringComparer.OrdinalIgnoreCase)
                && query?.Contains(key, StringComparison.OrdinalIgnoreCase) == true)
                return $"{key} ({slug})";
        return slug;
    }
}
