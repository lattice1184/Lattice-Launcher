using System.Text;
using System.Text.Json;

namespace Launcher.Core.Account;

/// <summary>
/// 8-13 Littleskin 第三方皮肤站登录（标准 Yggdrasil Connect 协议——Blessing Skin 系公开 API）：
/// 邮箱+密码 → authserver/authenticate → accessToken + selectedProfile（UUID/名/皮肤 textures）。
/// 密码只在内存中经 https 传输，不落盘不写日志。
/// </summary>
public static class LittleskinAuth
{
    private const string AuthenticateUrl = "https://littleskin.cn/api/yggdrasil/authserver/authenticate";

    /// <summary>登录结果：正版格式无横线 UUID（落盘前由 AccountService.FormatUuid 统一）+ 皮肤直链</summary>
    public sealed record LittleskinSession(string Uuid, string Name, string? SkinUrl);

    /// <summary>邮箱+密码登录。失败抛 InvalidOperationException（人话错误）。</summary>
    public static async Task<LittleskinSession> AuthenticateAsync(
        HttpClient http, string email, string password, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            // 8-13 真机修复：agent 是 Yggdrasil 必填项（服务端校验「agent must be an object」）
            agent = new { name = "Minecraft", version = 1 },
            username = email.Trim(),
            password,
            clientToken = Guid.NewGuid().ToString(),
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, AuthenticateUrl);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("selectedProfile", out var profile))
        {
            // Yggdrasil 标准错误：{"error":"...","errorMessage":"..."}
            var err = root.TryGetProperty("error", out var e) ? e.GetString() : "";
            var msg = root.TryGetProperty("errorMessage", out var m) ? m.GetString() : "";
            // 8-13 真机：认证通过但账号无角色（Littleskin 需先建角色——角色名=游戏名）时给专门引导
            if (msg.Contains("player", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "账号还没有角色：到 littleskin.cn 登录后，在「用户中心 → 角色管理」创建一个角色（角色名就是游戏名），再回来登录");
            throw new InvalidOperationException(err == "ForbiddenOperationException"
                ? $"邮箱或密码错误：{msg}"
                : $"登录失败：{err} {msg}");
        }

        var uuid = profile.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
        var name = profile.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
        if (uuid.Length == 0 || name.Length == 0)
            throw new InvalidOperationException("Littleskin 返回的角色数据不完整");

        // textures 属性：base64(JSON) → {"textures":{"SKIN":{"url":"https://littleskin.cn/textures/..."}}}
        string? skinUrl = null;
        if (profile.TryGetProperty("properties", out var props))
        {
            foreach (var p in props.EnumerateArray())
            {
                if (p.TryGetProperty("name", out var pn) && pn.GetString() != "textures") continue;
                if (!p.TryGetProperty("value", out var val)) continue;
                try
                {
                    var texturesJson = Encoding.UTF8.GetString(Convert.FromBase64String(val.GetString() ?? ""));
                    using var tex = JsonDocument.Parse(texturesJson);
                    if (tex.RootElement.TryGetProperty("textures", out var t)
                        && t.TryGetProperty("SKIN", out var skin)
                        && skin.TryGetProperty("url", out var url))
                    {
                        skinUrl = url.GetString();
                    }
                }
                catch { /* textures 损坏忽略，皮肤走 minotar 兜底 */ }
                break;
            }
        }

        return new LittleskinSession(uuid, name, skinUrl);
    }
}
