using System.Net.Http.Headers;
using System.Text.Json;

namespace Launcher.Core.Account;

/// <summary>
/// 微软正版登录（PCL/HMCL 同款设备码流程）：
/// 设备码 → 用户浏览器授权 → 轮询拿 OAuth token → Xbox Live → XSTS → Minecraft 认证链 → 正版 UUID + 用户名。
/// 使用 Mojang 官方启动器的公开 Azure client_id（社区通用，无需自行注册应用）；
/// 本启动器自己持有 refresh_token（轮换保存）→ 静默刷新，避免反复要求重新登录。
/// </summary>
public static class MicrosoftAuth
{
    // Mojang 官方 Minecraft 启动器的 Azure AD 应用（社区广泛使用，设备码流程仅需 client_id）
    private const string ClientId = "00000000402B532E";
    private const string Scope = "XboxLive.signin offline_access";

    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XboxAuthUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string MinecraftProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    /// <summary>设备码响应（用户在浏览器输入 user_code 完成授权）</summary>
    public sealed record DeviceCodeInfo(
        string UserCode, string VerificationUri, string DeviceCode, int Interval, int ExpiresIn);

    /// <summary>完整正版会话（含 refresh_token——持久化后静默刷新）</summary>
    public sealed record MicrosoftSession(
        string AccessToken, string RefreshToken, string MinecraftUuid, string MinecraftName);

    // ---------- 阶段 1：申请设备码 ----------

    public static async Task<DeviceCodeInfo> RequestDeviceCodeAsync(HttpClient http, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Scope,
        });
        using var resp = await http.PostAsync(DeviceCodeUrl, form, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException($"申请设备码失败: {err.GetString()}（{root.GetProperty("error_description").GetString()}）");
        return new DeviceCodeInfo(
            root.GetProperty("user_code").GetString() ?? "",
            root.GetProperty("verification_uri").GetString() ?? "https://microsoft.com/link",
            root.GetProperty("device_code").GetString() ?? "",
            root.GetProperty("interval").GetInt32(),
            root.GetProperty("expires_in").GetInt32());
    }

    // ---------- 阶段 2：轮询授权结果（用户输码后自动返回；15 分钟超时） ----------

    public static async Task<string> PollOAuthTokenAsync(HttpClient http, DeviceCodeInfo device, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(device.ExpiresIn);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = ClientId,
                ["device_code"] = device.DeviceCode,
            });
            using var resp = await http.PostAsync(TokenUrl, form, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("access_token", out var at))
                return at.GetString() ?? throw new InvalidOperationException("授权成功但未返回 token");
            if (root.TryGetProperty("error", out var err))
            {
                var code = err.GetString();
                if (code is "authorization_pending" or "slow_down") { /* 继续轮询 */ }
                else if (code == "authorization_declined")
                    throw new OperationCanceledException("用户拒绝了授权");
                else if (code == "expired_token")
                    throw new TimeoutException("设备码已过期，请重新登录");
                else
                    throw new InvalidOperationException($"授权失败: {code}");
            }
            await Task.Delay(TimeSpan.FromSeconds(device.Interval), ct);
        }
        throw new TimeoutException("设备码已过期，请重新登录");
    }

    // ---------- 阶段 3：OAuth token → Xbox/XSTS → Minecraft 认证链 ----------

    /// <summary>OAuth access token → 完整正版会话（uuid + 用户名）</summary>
    public static async Task<MicrosoftSession> AuthenticateMinecraftAsync(
        HttpClient http, string oauthAccessToken, string refreshToken, CancellationToken ct)
    {
        var xbox = await PostJsonAsync(http, XboxAuthUrl, new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = oauthAccessToken,
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        }, ct);

        // XSTS：Token + Xui[0].uhs（UserHash）；identityToken 格式：XBL3.0 x=<uhs>;<xstsToken>
        var (xstsToken, uhs) = await PostXstsAsync(http, xbox, ct);
        var minecraft = await PostJsonAsync(http, MinecraftLoginUrl, new
        {
            identityToken = $"XBL3.0 x={uhs};{xstsToken}",
        }, ct);

        // 正版 UUID + 用户名
        using var req = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraft);
        using var resp = await http.SendAsync(req, ct);
        var profileJson = await resp.Content.ReadAsStringAsync(ct);
        using var profileDoc = JsonDocument.Parse(profileJson);
        var profile = profileDoc.RootElement;
        if (!profile.TryGetProperty("id", out _))
            throw new InvalidOperationException($"未获取到正版档案（{profile.GetProperty("error").GetString()}）——可能未购买 Minecraft");

        return new MicrosoftSession(
            minecraft,
            refreshToken,
            profile.GetProperty("id").GetString() ?? "",
            profile.GetProperty("name").GetString() ?? "");
    }

    /// <summary>静默刷新：refresh_token 换新 OAuth token（轮换保存新 refresh_token）→ 重走认证链</summary>
    public static async Task<MicrosoftSession> RefreshAsync(
        HttpClient http, string refreshToken, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope,
        });
        using var resp = await http.PostAsync(TokenUrl, form, ct);
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
        var uhs = root.GetProperty("Xui")[0].GetProperty("uhs").GetString()
            ?? throw new InvalidOperationException("XSTS 响应缺少 UserHash");
        return (t.GetString()!, uhs);
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
