using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Launcher.Core.Download;

/// <summary>
/// 从 URL / Content-Disposition 响应头识别下载文件名（第三方文件下载用）。
/// </summary>
public static class UriFileNameResolver
{
    /// <summary>匹配 filename*="..." / filename*=token（RFC 5987，优先）</summary>
    private static readonly Regex StarName = new(
        @"filename\*\s*=\s*(?:""([^""]*)""|([^;""]*))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>匹配 filename="..." / filename=token（回退）</summary>
    private static readonly Regex PlainName = new(
        @"filename\s*=\s*(?:""([^""]*)""|([^;""]*))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>从 URL 路径取最后一段（解码 %20 等）；无路径段或结尾 / → null</summary>
    public static string? FromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.AbsolutePath))
            return null;
        var last = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
        return last.Length == 0 ? null : Sanitize(Uri.UnescapeDataString(last));
    }

    /// <summary>
    /// HEAD 拿 Content-Disposition 识别文件名：filename*（RFC 5987）优先，filename 回退。
    /// 请求失败 / 无头 / 解析不出 → null（调用方回退 URL 段）。
    /// </summary>
    public static async Task<string?> TryFromContentDispositionAsync(HttpClient client, string url,
        CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            var header = resp.Headers.TryGetValues("Content-Disposition", out var h) ? h.FirstOrDefault()
                : resp.Content.Headers.TryGetValues("Content-Disposition", out var c) ? c.FirstOrDefault()
                : null;
            return ParseContentDisposition(header);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>解析 Content-Disposition 头里的文件名；无 → null</summary>
    public static string? ParseContentDisposition(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        var star = Match(header, StarName);
        if (star is not null)
        {
            // 形如 UTF-8''abc%20def：去掉编码声明段再解码（RFC 5987）
            var idx = star.IndexOf("''", StringComparison.Ordinal);
            var raw = idx >= 0 ? star[(idx + 2)..] : star;
            try
            {
                return Sanitize(Uri.UnescapeDataString(raw));
            }
            catch
            {
                // 解码失败回退 plain
            }
        }
        return Match(header, PlainName) is { } plain ? Sanitize(plain) : null;
    }

    private static string? Match(string header, Regex rx)
    {
        var m = rx.Match(header);
        if (!m.Success) return null;
        var value = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        return value.Trim();
    }

    /// <summary>剔除 Windows 非法文件名字符（防路径穿越/无效名）；结果空 → null</summary>
    public static string? Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        var clean = sb.ToString().Trim();
        return clean.Length == 0 ? null : clean;
    }
}
