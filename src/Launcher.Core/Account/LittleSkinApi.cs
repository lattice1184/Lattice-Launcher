using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Launcher.Core.Account;

/// <summary>
/// LittleSkin 开放 API 客户端（8-16 批次 51 皮肤库：衣柜读 + 角色写）。
/// 端点事实（2026-08-15 调研 + 真机验证）：Base https://littleskin.cn/api，Bearer token 鉴权；
/// 预览图 /preview/{tid} 免 token；皮肤原图 /skin/{name}.png（yggdrasil 纹理路径）。
/// </summary>
public sealed class LittleSkinApi
{
    private const string BaseUrl = "https://littleskin.cn/api";
    private const string SkinFileBase = "https://littleskin.cn";

    private readonly HttpClient _http;
    private readonly Func<string?> _accessToken; // 每次请求现读（VM 侧刷新后即时生效）

    public LittleSkinApi(HttpClient http, Func<string?> accessToken)
    {
        _http = http;
        _accessToken = accessToken;
    }

    /// <summary>衣柜条目（Type: "steve"/"alex"）</summary>
    public sealed record ClosetItem(int Tid, string Name, string Type, string Hash,
        long Size, string Uploader, bool Public, DateTime UploadAt);

    /// <summary>衣柜分页（HasMore = 当前页满页——服务端分页无总数字段）</summary>
    public sealed record ClosetPage(IReadOnlyList<ClosetItem> Items, bool HasMore);

    /// <summary>401 专用异常：调用方（VM）捕获后刷新 token 重试一次</summary>
    public sealed class UnauthorizedException : InvalidOperationException
    {
        public UnauthorizedException(string message) : base(message) { }
    }

    /// <summary>衣柜列表（category=skin|cape）</summary>
    public async Task<ClosetPage> GetClosetAsync(string category, int page, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{BaseUrl}/closet?category={Uri.EscapeDataString(category)}&page={page}", ct);
        // 8-19 防御：TryGetProperty 对非 Object 根直接抛（"requires an element of type 'Object'"）——
        // 接口可能返回数组根（无 data 包装），先判 ValueKind 再取
        var root = doc.RootElement;
        var data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) ? d : root;
        if (data.ValueKind != JsonValueKind.Array)
            throw new HttpRequestException("LittleSkin 返回了无法解析的衣柜数据");
        var items = new List<ClosetItem>();
        foreach (var el in data.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            items.Add(new ClosetItem(
                el.TryGetProperty("tid", out var t) && t.TryGetInt32(out var tid) ? tid : 0,
                el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                el.TryGetProperty("type", out var ty) ? ty.GetString() ?? "steve" : "steve",
                el.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "",
                el.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0,
                el.TryGetProperty("uploader", out var u) ? u.GetString() ?? "" : "",
                el.TryGetProperty("public", out var pb) ? pb.GetBoolean() : false,
                el.TryGetProperty("upload_at", out var ua) && DateTime.TryParse(ua.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : default));
        }
        return new ClosetPage(items, items.Count > 0 && data.GetArrayLength() >= 20); // 满页（PageSize=20）即还有下一页
    }

    /// <summary>角色列表（GET /api/players；实机 8-19：返回数组根，无 data 包装——先判 ValueKind 再 TryGetProperty）</summary>
    public async Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{BaseUrl}/players", ct);
        var root = doc.RootElement;
        var data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) ? d : root;
        if (data.ValueKind != JsonValueKind.Array) return [];
        var list = new List<PlayerInfo>();
        foreach (var el in data.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var pid = el.TryGetProperty("pid", out var p) && p.TryGetInt32(out var v) ? v : 0;
            var name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (pid > 0 && !string.IsNullOrEmpty(name)) list.Add(new PlayerInfo(pid, name));
        }
        return list;
    }

    /// <summary>
    /// 8-18 角色名 → yggdrasil UUID（GET users/profiles/minecraft/{name}——LittleSkin 实现 yggdrasil 标准）。
    /// 拉取失败回退离线式 UUID（MD5(name) 定长格式化——与离线登录同款），保证连接即登录不阻塞。
    /// </summary>
    public async Task<string> GetUuidByNameAsync(string name, CancellationToken ct)
    {
        try
        {
            using var doc = await GetJsonAsync($"{BaseUrl}/users/profiles/minecraft/{Uri.EscapeDataString(name)}", ct);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                var uuid = id.GetString();
                if (!string.IsNullOrWhiteSpace(uuid)) return uuid;
            }
        }
        catch { /* 拉取失败回退离线式 */ }
        return OfflineUuid(name);
    }

    /// <summary>离线式 UUID（MD5(name) → 8-4-4-4-12 格式）——yggdrasil 拉不到时的兜底</summary>
    private static string OfflineUuid(string name)
    {
        var md5 = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        var hex = Convert.ToHexString(md5).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    /// <summary>应用皮肤到角色（PUT /api/players/{pid}/textures body {"skin": tid}）——游戏内 yggdrasil 立即生效</summary>
    public async Task ApplySkinAsync(int pid, int tid, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/players/{pid}/textures")
        {
            Content = new StringContent($"{{\"skin\":{tid}}}", Encoding.UTF8, "application/json"),
        };
        using var resp = await SendWithAuthAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            await ThrowForAsync(resp, "应用皮肤失败", ct);
    }

    /// <summary>缩略图 URL（免 token，ImageLoader 直接复用）</summary>
    public static string PreviewUrl(int tid) => $"{SkinFileBase}/preview/{tid}";

    /// <summary>皮肤原图 URL（yggdrasil 纹理路径；角色没应用过纹理时可能 404，调用方降级 /textures/{hash}）</summary>
    public static string SkinFileUrl(string playerName) => $"{SkinFileBase}/skin/{Uri.EscapeDataString(playerName)}.png";

    // ---------- 内部 ----------

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await SendWithAuthAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                try { return JsonDocument.Parse(json); }
                catch (JsonException) { throw new HttpRequestException("LittleSkin 响应格式异常，请稍后重试"); }
            }
            if (attempt == 0 && (int)resp.StatusCode is 404 or >= 500)
            {
                await Task.Delay(500, ct); // 瞬时故障重试一次（CF 模板）
                continue;
            }
            await ThrowForAsync(resp, "读取 LittleSkin 数据失败", ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>带 Bearer 头发送（token 每次现读）</summary>
    private async Task<HttpResponseMessage> SendWithAuthAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var token = _accessToken();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("LittleSkin 未连接，请先连接账号");
        req.Headers.Add("Authorization", "Bearer " + token);
        return await _http.SendAsync(req, ct);
    }

    /// <summary>非 2xx → 人话异常（401 明确提示重新连接；其余带响应体消息）</summary>
    private static async Task ThrowForAsync(HttpResponseMessage resp, string prefix, CancellationToken ct)
    {
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedException("LittleSkin 授权已过期");
        string? body = null;
        try { body = await resp.Content.ReadAsStringAsync(ct); } catch { }
        var msg = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var m)) msg = m.GetString() ?? "";
                else if (doc.RootElement.TryGetProperty("error", out var e)) msg = e.GetString() ?? "";
            }
            catch { msg = body.Length > 120 ? body[..120] : body; }
        }
        throw new HttpRequestException($"{prefix}（HTTP {(int)resp.StatusCode}）{msg}");
    }
}

/// <summary>LittleSkin 角色（pid 用于应用皮肤 PUT；顶层 record——XAML DataTemplate 可引用）</summary>
public sealed record PlayerInfo(int Pid, string Name);
