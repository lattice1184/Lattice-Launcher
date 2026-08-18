using Launcher.Core.Launch;

namespace Launcher.Core.Account;

/// <summary>
/// 8-19 LittleSkin 皮肤本地同步（登录/皮肤库共用）：下载角色皮肤
/// （yggdrasil 纹理路径 → 降级 /textures/{hash}）→ 校验尺寸 → 写 %AppData%\Launcher\skins\{name}.png。
/// SkinPack 注入条件 = 本地文件存在——OAuth 登录后不同步则游戏内是默认 Steve/Alex（旧邮箱流程有、重构丢失的回归）。
/// </summary>
public static class LittleSkinSkinSync
{
    /// <summary>本地皮肤路径（与 HomeViewModel.LocalSkinPath 同规则）</summary>
    public static string LocalSkinPath(string playerName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Launcher", "skins", $"{playerName}.png");

    /// <summary>下载皮肤写本地；返回是否成功（下载失败/尺寸不合规 → false，调用方决定兜底）</summary>
    public static async Task<bool> DownloadToLocalAsync(HttpClient http, string playerName, string? fallbackHash = null)
    {
        byte[]? bytes = null;
        foreach (var url in BuildUrls(playerName, fallbackHash))
        {
            try
            {
                using var resp = await http.GetAsync(url);
                if (resp.IsSuccessStatusCode) { bytes = await resp.Content.ReadAsByteArrayAsync(); break; }
            }
            catch { /* 换下一个候选 */ }
        }
        if (bytes is null) return false;

        var size = SkinPngHeader.TryParse(bytes);
        if (size is not { } dims || !SkinPack.IsSupportedSize(dims.Width, dims.Height))
            return false; // 尺寸不支持（或非 PNG）——不写本地（游戏内 PUT 已生效）

        SkinFileWriter.ForceWrite(LocalSkinPath(playerName), bytes);
        return true;
    }

    private static IEnumerable<string> BuildUrls(string playerName, string? fallbackHash)
    {
        yield return LittleSkinApi.SkinFileUrl(playerName);
        if (!string.IsNullOrWhiteSpace(fallbackHash))
            yield return $"https://littleskin.cn/textures/{fallbackHash}";
    }
}
