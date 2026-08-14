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
/// </summary>
public sealed class ThirdPartyDlSourceResolver : IDlSourceResolver
{
    /// <summary>加速镜像（按实测速度排序；前缀即 URL 格式：{镜像}/{原URL}）</summary>
    public static readonly string[] Mirrors =
    [
        "https://ghproxy.net",
        "https://gh-proxy.com",
    ];

    public IReadOnlyList<string> Resolve(string officialUrl)
    {
        if (!IsGitHubUrl(officialUrl)) return [officialUrl];
        var list = new List<string>(Mirrors.Length + 2) { officialUrl };
        foreach (var mirror in Mirrors)
            list.Add($"{mirror}/{officialUrl}");
        // 黑科技 A：GitHub API 官方直链占位（ghapi:{o}/{r}/{tag}/{name}，下载前换链）——
        // 仅 release 直链可用（签名 URL 已是最终直链，无需 API 换链）
        if (IsGitHubRelease(officialUrl))
            list.Add($"{GitHubApiDirect.Scheme}{ToGhapiPath(officialUrl)}");
        return list;
    }

    /// <summary>
    /// GitHub 文件直链（release 下载/展开资产 + 签名 CDN 直链——签名 URL 国内直连慢，镜像竞速同样受益）。
    /// github.com 分支保留 release 文件特征（tag/列表页是 HTML，不算文件直链——08-10 语义）。
    /// </summary>
    public static bool IsGitHubUrl(string url) =>
        (url.StartsWith("https://github.com/")
         && (url.Contains("/releases/download/") || url.Contains("/releases/expanded_assets/")))
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
