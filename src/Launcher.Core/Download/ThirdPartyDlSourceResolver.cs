namespace Launcher.Core.Download;

/// <summary>
/// 第三方下载的 GitHub 加速 resolver：GitHub 直链（release + 签名 CDN）映射为多候选
/// （原 URL + 国内加速镜像），交给 DownloadService 的 AL32 并行竞速——原 URL 卡住时镜像先到先得。
/// 非 GitHub 链接保持单候选直连（原行为）。
/// 背景（08-10 实机）：github.com 直连国内被墙/干扰（curl 21s 超时），release 直链第一步 302 就死；
/// 镜像列表实测 ghproxy.net 转发 1.17s 且支持 Range（206 分片可用），gh-proxy.com 存活；
/// 已挂废弃：ghproxy.com、ghfast.top、ghproxy.cc、mirror.ghproxy.com、ghproxy.miguan.cc。
/// 8-15 扩展：objects.githubusercontent.com / codeload.github.com 签名直链也走镜像竞速
/// （ghapi 换链出的签名 URL 国内直连几十 KB/s，套镜像显著提速）。
/// 8-18 扩展：github.com/.../archive/ 打包下载（zip/tar.gz）也进镜像竞速——此前漏匹配走单候选直连
/// （用户实测 deepseek-harness archive 下载失效的根因）；实测 ghp.ci / kkgithub.com 当前死源不入列。
/// </summary>
public sealed class ThirdPartyDlSourceResolver : IDlSourceResolver
{
    /// <summary>加速镜像（按实测速度排序；前缀即 URL 格式：{镜像}/{原URL}）</summary>
    public static readonly string[] Mirrors =
    [
        "https://ghproxy.net",
        "https://gh-proxy.com",
    ];

    /// <summary>失败记忆窗（毫秒，8-18）：本轮失败过的源在此窗内排候选末位——死镜像不再每轮白花 8s HEAD 超时</summary>
    internal const long FailureMemoryMs = 30_000;

    /// <summary>host → 最近失败时间戳（TickCount64；进程内，不持久化）</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> FailedHosts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否值得记忆（GitHub 直链或镜像包装 URL——镜像 URL 的 host 是镜像域，不能按 IsGitHubUrl 判）</summary>
    private static bool IsTrackable(string url) =>
        IsGitHubUrl(url) || Mirrors.Any(m => url.StartsWith(m + "/", StringComparison.Ordinal));

    /// <summary>标记候选源失败（DownloadService 候选抛错/淘汰处调用；host 级记忆；非 GitHub 源不记——Resolve 不查询）</summary>
    public static void MarkFailed(string url)
    {
        if (!IsTrackable(url)) return;
        try { FailedHosts[new Uri(url).Host] = Environment.TickCount64; } catch { /* 解析失败不记 */ }
    }

    /// <summary>该 URL 的 host 是否在失败记忆窗内（internal 供测试直查）</summary>
    internal static bool IsInFailureWindow(string url)
    {
        try
        {
            return FailedHosts.TryGetValue(new Uri(url).Host, out var t)
                && Environment.TickCount64 - t < FailureMemoryMs;
        }
        catch { return false; }
    }

    /// <summary>清空失败记忆（测试隔离用）</summary>
    internal static void ClearFailures() => FailedHosts.Clear();

    public IReadOnlyList<string> Resolve(string officialUrl)
    {
        if (!IsGitHubUrl(officialUrl)) return [officialUrl];
        // 8-18 失败记忆：失败窗内的官方源/镜像排末位（不剔除——其他候选全挂时兜底仍可用）。
        // 顺序：fresh 官方 → fresh 镜像 → stale 官方 → stale 镜像；死镜像不再每轮白花 8s HEAD 超时
        var officialStale = IsInFailureWindow(officialUrl);
        var (fresh, stale) = (new List<string>(), new List<string>());
        foreach (var mirror in Mirrors)
            (IsInFailureWindow($"{mirror}/{officialUrl}") ? stale : fresh).Add(mirror);
        var list = new List<string>(Mirrors.Length + 2);
        if (!officialStale) list.Add(officialUrl);
        foreach (var mirror in fresh) list.Add($"{mirror}/{officialUrl}");
        if (officialStale) list.Add(officialUrl);
        foreach (var mirror in stale) list.Add($"{mirror}/{officialUrl}");
        // 黑科技 A：GitHub API 官方直链占位（ghapi:{o}/{r}/{tag}/{name}，下载前换链）——
        // 仅 release 直链可用（签名 URL 已是最终直链，无需 API 换链）
        if (IsGitHubRelease(officialUrl))
            list.Add($"{GitHubApiDirect.Scheme}{ToGhapiPath(officialUrl)}");
        return list;
    }

    /// <summary>
    /// GitHub 文件直链（release 下载/展开资产 + 签名 CDN 直链 + archive 打包——签名 URL 国内直连慢，镜像竞速同样受益）。
    /// github.com 分支保留文件特征（tag/列表页是 HTML，不算文件直链——08-10 语义）；
    /// 8-18 加 archive：/archive/ 是打包下载路径（zip/tar.gz/zipball/tarball），镜像转发实测可用。
    /// </summary>
    public static bool IsGitHubUrl(string url) =>
        (url.StartsWith("https://github.com/")
         && (url.Contains("/releases/download/") || url.Contains("/releases/expanded_assets/")
             || url.Contains("/archive/")))
        || url.StartsWith("https://objects.githubusercontent.com/")
        || url.StartsWith("https://codeload.github.com/");

    /// <summary>release 直链 → ghapi:{owner}/{repo}/{tag}/{name}</summary>
    private static string ToGhapiPath(string url)
    {
        // https://github.com/{o}/{r}/releases/download/{tag}/{name}
        var seg = new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // seg: [o, r, releases, download|expanded_assets, tag, name...]
        var name = string.Join("/", seg.Skip(5));
        return $"{seg[0]}/{seg[1]}/{seg[4]}/{name}";
    }

    /// <summary>GitHub release 文件直链特征（tag 页面 HTML 不算——只有 download/expanded_assets 是文件）</summary>
    private static bool IsGitHubRelease(string url) =>
        url.StartsWith("https://github.com/")
        && (url.Contains("/releases/download/") || url.Contains("/releases/expanded_assets/"));
}
