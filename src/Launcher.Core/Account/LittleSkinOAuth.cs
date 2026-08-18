using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Launcher.Core.Account;

/// <summary>
/// LittleSkin OAuth2 设备码流（RFC 8628，8-16 批次 51 皮肤库）。
/// 结构照抄 MicrosoftAuth 设备码流：发起 → 用户浏览器输码 → 轮询。区别：只要 client_id 无需 secret，
/// 端点域为 open.littleskin.cn（2026-08-15 curl 真机验证 invalid_client/invalid_request 标准响应）。
/// </summary>
public static class LittleSkinOAuth
{
    // 端点集中常量（8-16 真机验证可用；域或路径变动只改这里）
    internal const string DeviceCodeUrl = "https://open.littleskin.cn/oauth/device_code";
    internal const string TokenUrl = "https://open.littleskin.cn/oauth/token";

    /// <summary>
    /// 连接所需 scope：衣柜读 + 角色读写 + offline_access（8-16 文档核实：refresh_token 必须申请
    /// offline_access，否则拿不到刷新令牌、401 自愈链路失效；设备码流勿申请 Yggdrasil scope——invalid_scope）
    /// </summary>
    public const string ConnectScope = "Closet.Read Player.ReadWrite offline_access";

    /// <summary>设备码会话（verification_uri_complete 可直接开浏览器）</summary>
    public sealed record DeviceCodeSession(
        string UserCode, string DeviceCode, string VerificationUri, string VerificationUriComplete,
        int IntervalSec, int ExpiresInSec);

    /// <summary>token 对（refresh_token 轮换：响应含新值则更新）</summary>
    public sealed record TokenPair(string AccessToken, string RefreshToken, int ExpiresInSec);

    /// <summary>发起设备码：POST form {client_id, scope}。失败抛 InvalidOperationException（人话）</summary>
    public static async Task<DeviceCodeSession> StartDeviceCodeAsync(HttpClient http, string clientId, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = ConnectScope,
        });
        using var resp = await http.PostAsync(DeviceCodeUrl, form, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("user_code", out var userCode))
        {
            var err = root.TryGetProperty("error", out var e) ? e.GetString() : "";
            var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : "";
            Launcher.Core.Utils.AppLog.Instance?.LogWarning("[littleskin] device code start failed: {Error}", err);
            if (err == "invalid_client")
                throw new InvalidOperationException(
                    "LittleSkin 拒绝了应用 ID（client_id 无效）。请去 littleskin.cn 用户中心创建 OAuth 应用，把 client_id 填进设置页");
            throw new InvalidOperationException($"连接 LittleSkin 失败：{err}（{desc}）");
        }
        Launcher.Core.Utils.AppLog.Instance?.LogInformation("[littleskin] device code session started");
        return new DeviceCodeSession(
            userCode.GetString()!,
            root.TryGetProperty("device_code", out var dc) ? dc.GetString() ?? "" : "",
            root.TryGetProperty("verification_uri", out var vuri) ? vuri.GetString() ?? "https://open.littleskin.cn/oauth/link" : "https://open.littleskin.cn/oauth/link",
            root.TryGetProperty("verification_uri_complete", out var vc) ? vc.GetString() ?? "" : "",
            root.TryGetProperty("interval", out var iv) ? Math.Max(3, iv.GetInt32()) : 5,
            root.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 900);
    }

    /// <summary>
    /// 轮询授权结果（每 intervalSec 一次，直到成功/过期/取消）。
    /// authorization_pending = 用户还没在浏览器输码 → 继续等；slow_down → 加回建议间隔；其他错误 → 人话抛。
    /// 超时 → TimeoutException；取消 → OperationCanceledException。
    /// </summary>
    public static async Task<TokenPair> PollDeviceCodeAsync(
        HttpClient http, string clientId, DeviceCodeSession session,
        Action<string>? onTick = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Min(session.ExpiresInSec, 900));
        var intervalSec = session.IntervalSec <= 3 ? session.IntervalSec : 3;
        var lastStatus = "";
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["device_code"] = session.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            });
            using var resp = await http.PostAsync(TokenUrl, form, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("access_token", out var at))
            {
                var rt = root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? "" : "";
                if (rt.Length == 0)
                    throw new InvalidOperationException("授权成功但未返回 refresh_token");
                var expires = root.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 3600;
                return new TokenPair(at.GetString()!, rt, expires);
            }
            if (root.TryGetProperty("error", out var err))
            {
                var code = err.GetString() ?? "";
                if (code == "authorization_pending")
                {
                    var status = "等待你在浏览器中输入代码并授权…";
                    if (status != lastStatus) { lastStatus = status; onTick?.Invoke(status); }
                }
                else if (code == "slow_down")
                {
                    intervalSec = session.IntervalSec; // 服务端要求降频：回建议间隔
                }
                else
                {
                    var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : "";
                    throw code switch
                    {
                        "access_denied" => new InvalidOperationException("你拒绝了 LittleSkin 授权，连接已取消"),
                        "expired_token" => new InvalidOperationException("授权已过期，请重新发起连接"),
                        _ => new InvalidOperationException($"LittleSkin 授权失败：{code}（{desc}）"),
                    };
                }
            }
            else
            {
                throw new InvalidOperationException("LittleSkin 返回了无法解析的授权响应");
            }
            await Task.Delay(TimeSpan.FromSeconds(intervalSec), ct);
        }
        throw new TimeoutException("授权超时，请重新发起连接");
    }

    /// <summary>刷新 token：invalid_grant（已失效）→ 人话抛，调用方清 token 回未连接态</summary>
    public static async Task<TokenPair> RefreshAsync(HttpClient http, string clientId, string refreshToken, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        });
        using var resp = await http.PostAsync(TokenUrl, form, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("access_token", out var at))
        {
            var rt = root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? "" : "";
            var expires = root.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 3600;
            return new TokenPair(at.GetString()!, rt.Length > 0 ? rt : refreshToken, expires); // 无新 refresh_token 则沿用旧的
        }
        var code = root.TryGetProperty("error", out var e) ? e.GetString() : "";
        var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : "";
        if (code == "invalid_grant")
            throw new InvalidOperationException("LittleSkin 登录已失效，请重新连接");
        throw new InvalidOperationException($"刷新 LittleSkin 授权失败：{code}（{desc}）");
    }
}
