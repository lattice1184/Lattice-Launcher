using System.Net.Http.Headers;
using System.Text.Json;

namespace Launcher.Core.Account;

/// <summary>
/// 微软正版登录（8-13 重写为 Live 设备码流，与主流启动器同款）：
/// POST oauth20_connect.srf 拿一次性配对码（user_code，微软服务器生成的 8 位码）→
/// 用户在浏览器 microsoft.com/link 输入该码并登录授权 →
/// 本程序轮询 oauth20_token.srf 拿 Live token（MBI_SSL scope）→
/// RPS 交换（t= 前缀）XBL user token → XSTS → Minecraft 认证链。
/// 本启动器自己持有 refresh_token（轮换保存）→ 静默刷新，避免反复要求重新登录。
/// clientId 设置可配（LauncherSettings.MicrosoftClientId）——微软会继续收紧，「选择交给使用者」。
/// </summary>
public static class MicrosoftAuth
{
    // 8-13：Minecraft Java 官方 title client id（Live 设备码端点 oauth20_connect.srf 实测可用；
    // 部分老 Live 系 clientId 只启用 remoteconnect 端点，不支持设备码——invalid_client）
    internal const string FallbackClientId = "00000000402b5328";
    // 设备码流程固定 scope：MBI_SSL = Xbox Live 用户令牌（wl.* 不适用于设备码端点——invalid_scope）
    private const string DeviceCodeScope = "service::user.auth.xboxlive.com::MBI_SSL";

    /// <summary>8-13 进程内解析值（ClientIdRemote.ResolveAsync 写入；null = 内置兜底）</summary>
    private static string? _resolved;

    /// <summary>8-13 三层取值：设置手动值 > 远程/缓存解析值 > 内置兜底（设置每次现读——改完即时生效）</summary>
    internal static string EffectiveClientId()
    {
        var id = Launcher.Core.Utils.LauncherSettings.Current.MicrosoftClientId;
        if (!string.IsNullOrWhiteSpace(id)) return id.Trim();
        return _resolved is { Length: > 0 } ? _resolved : FallbackClientId;
    }

    /// <summary>写入进程内解析值（ClientIdRemote 用——测试可直调）</summary>
    internal static void SetResolvedClientId(string id) => _resolved = id;

    private const string DeviceCodeRequestUrl = "https://login.live.com/oauth20_connect.srf";
    private const string LiveTokenUrl = "https://login.live.com/oauth20_token.srf";
    private const string XboxAuthUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string MinecraftProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    /// <summary>完整正版会话（含 refresh_token——持久化后静默刷新）。
    /// ExpiresAtUtc = Minecraft token 过期时间（login_with_xbox 的 expires_in；未过期可直接启动，跳过刷新链）</summary>
    public sealed record MicrosoftSession(
        string AccessToken, string RefreshToken, string MinecraftUuid, string MinecraftName,
        DateTime ExpiresAtUtc = default);

    /// <summary>8-13 设备码会话：UserCode 给用户看（浏览器里输），DeviceCode 用于轮询</summary>
    public sealed record DeviceCodeSession(
        string UserCode, string DeviceCode, string VerificationUri, int IntervalSec, int ExpiresInSec);

    /// <summary>8-13 发起设备码登录：返回配对码 + 轮询凭证。发起失败抛错（invalid_client 等）。</summary>
    public static async Task<DeviceCodeSession> StartDeviceCodeAsync(HttpClient http, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["scope"] = DeviceCodeScope,
            ["client_id"] = EffectiveClientId(),
            ["response_type"] = "device_code",
        });
        using var resp = await http.PostAsync(DeviceCodeRequestUrl, form, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("user_code", out var userCode))
        {
            var err = root.TryGetProperty("error", out var e) ? e.GetString() : "";
            var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : "";
            throw new InvalidOperationException(
                $"发起设备码登录失败: {err}（{desc}）。可在设置里更换微软登录 Client ID");
        }
        return new DeviceCodeSession(
            userCode.GetString()!,
            root.TryGetProperty("device_code", out var dc) ? dc.GetString() ?? "" : "",
            root.TryGetProperty("verification_uri", out var vuri) ? vuri.GetString() ?? "" : "https://www.microsoft.com/link",
            root.TryGetProperty("interval", out var iv) ? Math.Max(3, iv.GetInt32()) : 5,
            root.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 900);
    }

    /// <summary>
    /// 8-13 轮询设备码授权结果（每 intervalSec 一次，直到成功/过期/取消）。
    /// authorization_pending = 用户还没在浏览器输码 → 继续等；其他错误 → 抛错。
    /// 成功返回 (access_token, refresh_token)。
    /// </summary>
    public static async Task<(string AccessToken, string RefreshToken)> PollDeviceCodeAsync(
        HttpClient http, DeviceCodeSession session, Action<string>? onTick = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Min(session.ExpiresInSec, 900));
        // 8-13 提速：前 3 分钟 3s 快轮询（授权盲区从 5s 降到 3s——用户输完码等启动器反应的体感），
        // 之后回微软建议间隔；微软若要求降频（slow_down）立即回 session.IntervalSec
        var fastWindowEnd = DateTime.UtcNow.AddMinutes(3);
        var intervalSec = session.IntervalSec <= 3 ? session.IntervalSec : 3;
        var lastStatus = "";
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = EffectiveClientId(),
                ["device_code"] = session.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            });
            using var resp = await http.PostAsync(
                LiveTokenUrl + "?client_id=" + Uri.EscapeDataString(EffectiveClientId()), form, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("access_token", out var at))
            {
                var rt = root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? "" : "";
                if (rt.Length == 0)
                    throw new InvalidOperationException("授权成功但未返回 refresh_token");
                return (at.GetString()!, rt);
            }
            if (root.TryGetProperty("error", out var err))
            {
                var code = err.GetString() ?? "";
                if (code == "authorization_pending")
                {
                    var status = "等待你在浏览器中输入代码并登录…";
                    if (status != lastStatus) { lastStatus = status; onTick?.Invoke(status); }
                }
                else if (code == "slow_down")
                {
                    intervalSec = session.IntervalSec; // 微软要求降频：回建议间隔
                }
                else
                {
                    var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : "";
                    throw new InvalidOperationException($"设备码授权失败: {code}（{desc}）");
                }
            }
            else
            {
                throw new InvalidOperationException("微软返回了无法解析的授权响应");
            }
            if (DateTime.UtcNow > fastWindowEnd) intervalSec = session.IntervalSec;
            await Task.Delay(TimeSpan.FromSeconds(intervalSec), ct);
        }
        throw new TimeoutException("登录码已过期，请重新发起登录");
    }

    /// <summary>OAuth token（MBI_SSL）→ 完整正版会话（uuid + 用户名）。
    /// onStage：认证链分步状态回调（登录 UI「正在认证…」实时反馈——缓解同步慢的体感）</summary>
    public static async Task<MicrosoftSession> AuthenticateMinecraftAsync(
        HttpClient http, string oauthAccessToken, string refreshToken, CancellationToken ct,
        Action<string>? onStage = null)
    {
        onStage?.Invoke("正在认证 Xbox…");
        var xbox = await PostJsonAsync(http, XboxAuthUrl, new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                // 8-13：设备码拿到的 MBI_SSL token 用 t= 前缀（d= 是 AAD access token 的前缀）
                RpsTicket = "t=" + oauthAccessToken,
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        }, ct);

        // XSTS：Token + DisplayClaims.xui[0].uhs（UserHash）；identityToken 格式：XBL3.0 x=<uhs>;<xstsToken>
        onStage?.Invoke("正在认证 XSTS…");
        var (xstsToken, uhs) = await PostXstsAsync(http, xbox, ct);
        onStage?.Invoke("正在登录 Minecraft…");

        // Minecraft token（login_with_xbox 响应带 expires_in——记录过期时间供启动前跳过刷新）
        using var mcReq = new HttpRequestMessage(HttpMethod.Post, MinecraftLoginUrl);
        mcReq.Content = new StringContent(JsonSerializer.Serialize(new
        {
            identityToken = $"XBL3.0 x={uhs};{xstsToken}",
        }), System.Text.Encoding.UTF8, "application/json");
        using var mcResp = await http.SendAsync(mcReq, ct);
        var mcJson = await mcResp.Content.ReadAsStringAsync(ct);
        using var mcDoc = JsonDocument.Parse(mcJson);
        var mcRoot = mcDoc.RootElement;
        if (!mcRoot.TryGetProperty("access_token", out var mcAt))
            throw new InvalidOperationException("Minecraft 登录失败（可能该账号未购买 Minecraft）");
        var minecraft = mcAt.GetString()!;
        var expiresIn = mcRoot.TryGetProperty("expires_in", out var eis) ? eis.GetInt64() : 86400L;
        var expiresAt = DateTime.UtcNow.AddSeconds(Math.Clamp(expiresIn, 60, 86400 * 7));

        // 正版 UUID + 用户名
        onStage?.Invoke("正在读取正版档案…");
        using var req = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraft);
        using var resp = await http.SendAsync(req, ct);
        var profileJson = await resp.Content.ReadAsStringAsync(ct);
        // 8-22 全栈排查：非 2xx（token 吊销 401/服务端 500）时错误体可能是 HTML 或
        // 无 error 键的 JSON——旧代码直接 Parse+GetProperty 抛 KeyNotFoundException/JsonException，
        // 用户看到「字典中不存在给定键」而非「请重新登录」
        if (!resp.IsSuccessStatusCode)
        {
            string detail = "（无法解析服务端错误）";
            try
            {
                using var errDoc = JsonDocument.Parse(profileJson);
                if (errDoc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == System.Text.Json.JsonValueKind.String)
                    detail = err.GetString()!;
            }
            catch { /* 非 JSON 错误体——用默认文案 */ }
            throw new InvalidOperationException($"正版档案获取失败（HTTP {(int)resp.StatusCode}：{detail}）——可能未购买 Minecraft 或登录已过期，请重新登录");
        }
        using var profileDoc = JsonDocument.Parse(profileJson);
        var profile = profileDoc.RootElement;
        if (!profile.TryGetProperty("id", out _))
            throw new InvalidOperationException("未获取到正版档案——可能未购买 Minecraft");

        return new MicrosoftSession(
            minecraft,
            refreshToken,
            profile.GetProperty("id").GetString() ?? "",
            profile.GetProperty("name").GetString() ?? "",
            expiresAt);
    }

    /// <summary>静默刷新：refresh_token 换新 Live token（MBI_SSL 同参数）→ 重走认证链</summary>
    public static async Task<MicrosoftSession> RefreshAsync(
        HttpClient http, string refreshToken, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = EffectiveClientId(),
            ["refresh_token"] = refreshToken,
            ["scope"] = DeviceCodeScope,
        });
        using var resp = await http.PostAsync(LiveTokenUrl, form, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("access_token", out var at))
            throw new InvalidOperationException("刷新登录已失效，请重新登录");
        var newRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : refreshToken;
        return await AuthenticateMinecraftAsync(http, at.GetString()!, newRefresh ?? refreshToken, ct);
    }

    /// <summary>XSTS 授权：返回 (Token, UserHash)</summary>
    private static async Task<(string Token, string Uhs)> PostXstsAsync(HttpClient http, string xboxToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, XstsUrl);
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xboxToken },
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT",
        }), System.Text.Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("XErr", out _) || !root.TryGetProperty("Token", out var t))
            throw new InvalidOperationException("XSTS 授权失败（可能该账号未购买 Minecraft）");
        // 8-13 修复：uhs 在 DisplayClaims.xui[0].uhs（顶层没有 Xui 键——真机 KeyNotFound 真凶；
        // user.auth/XSTS 的 XBL 响应都是这个结构）
        if (root.TryGetProperty("DisplayClaims", out var dc)
            && (dc.TryGetProperty("xui", out var xui) || dc.TryGetProperty("Xui", out xui))
            && xui.GetArrayLength() > 0
            && xui[0].TryGetProperty("uhs", out var uhsEl))
        {
            return (t.GetString()!, uhsEl.GetString() ?? "");
        }
        throw new InvalidOperationException("XSTS 响应缺少 UserHash");
    }

    // ---------- 工具 ----------

    private static async Task<string> PostJsonAsync(HttpClient http, string url, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("Token", out var t))
            return t.GetString() ?? throw new InvalidOperationException($"认证链失败: {url}");
        if (root.TryGetProperty("access_token", out var at))
            return at.GetString() ?? throw new InvalidOperationException($"认证链失败: {url}");
        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException($"认证失败: {err.GetString()}（{root.GetProperty("error_description").GetString()}）");
        throw new InvalidOperationException($"认证链响应异常: {url}");
    }
}
