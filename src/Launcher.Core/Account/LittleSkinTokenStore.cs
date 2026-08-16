using System.Text.Json;
using Launcher.Core.Utils;

namespace Launcher.Core.Account;

/// <summary>
/// LittleSkin OAuth token 持久化（8-16 批次 51 皮肤库）。
/// 独立于 AccountService：token 是「LittleSkin 账号 + OAuth 应用」级授权，与启动器当前登录账号无关。
/// 敏感字段 DPAPI 加密落盘（Secret 模板）；损坏/不存在 → Load 返回 null 不抛。
/// </summary>
public sealed class LittleSkinTokenStore
{
    public static LittleSkinTokenStore Shared { get; } = new();

    private readonly string _path;

    public LittleSkinTokenStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "littleskin-token.json");
    }

    private sealed record Stored(string AccessToken, string RefreshToken, int ExpiresInSec);

    /// <summary>读 token（解密）；无文件/损坏 → null</summary>
    public LittleSkinOAuth.TokenPair? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path));
            if (s is null) return null;
            var at = Secrets.Read(s.AccessToken);
            var rt = Secrets.Read(s.RefreshToken);
            if (string.IsNullOrWhiteSpace(at) || string.IsNullOrWhiteSpace(rt)) return null;
            return new LittleSkinOAuth.TokenPair(at, rt, s.ExpiresInSec);
        }
        catch { return null; }
    }

    /// <summary>写 token（DPAPI 加密落盘）</summary>
    public void Save(LittleSkinOAuth.TokenPair tokens)
    {
        var stored = new Stored(Secrets.Protect(tokens.AccessToken), Secrets.Protect(tokens.RefreshToken), tokens.ExpiresInSec);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(stored));
    }

    /// <summary>清除（断开连接）</summary>
    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* 删除失败不致命 */ }
    }
}
