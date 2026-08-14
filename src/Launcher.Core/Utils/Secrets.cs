using System.Security.Cryptography;
using System.Text;

namespace Launcher.Core.Utils;

/// <summary>
/// 本地机密保护：DPAPI 用户级加密（同一 Windows 账户才能解密，拷走文件也解不开）。
/// 存储格式带 "dpapi:" 前缀标记；无前缀 = 旧版明文数据（读取时原样返回，下次保存自动转加密）。
/// </summary>
public static class Secrets
{
    private const string Prefix = "dpapi:";

    /// <summary>加密为 "dpapi:" + base64；空串原样返回（不为空值存密文）。</summary>
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(enc);
    }

    /// <summary>读取：带前缀 → DPAPI 解密；无前缀 → 旧版明文迁移。解密失败（换账户/数据损坏）→ null，视为未配置。</summary>
    public static string? Read(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored; // 旧版明文
        try
        {
            var enc = Convert.FromBase64String(stored[Prefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return null;
        }
    }
}
