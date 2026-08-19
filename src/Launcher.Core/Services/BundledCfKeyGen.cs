using System.Security.Cryptography;
using System.Text;

namespace Launcher.Core.Services;

/// <summary>
/// 构建注入生成——勿手改/勿提交含 key 版本（发布.ps1 覆盖）。
/// 8-19 升级（对齐 PCL2 AES-HMAC 思路）：内置值 = AES-256-CBC + HMAC-SHA256 密文，
/// 密钥由内嵌常量派生（SHA256）——直接 grep 二进制搜不到密钥与明文（旧版 +7 移位可逆 30 秒还原）。
/// </summary>
internal static class BundledCfKeyGen
{
    /// <summary>构建注入字段：AES-HMAC|base64(iv+cipher+hmac)；空 = 无内置 key（用户自填兜底）</summary>
    public static readonly string Encrypted = "";

    /// <summary>预留字段（构建注入模板同步用）</summary>
    public static readonly string Decoy = "Nzg5Ojs8PT4/QGhpamtsbTc4OTo7PD0+P0BoaWprbG0=";

    /// <summary>密钥派生盐（发布脚本同款；改动必须两处同步——发布.ps1 的 Inject-BundledCfKey）</summary>
    private const string KeySalt = "Lattice.CfKey.Internal.v2";

    /// <summary>加密镜像（测试往返用；与发布.ps1 Inject-BundledCfKey 的 PowerShell 实现一一对应：
    /// iv(16)|hmac(32)|cipher，AES-256-CBC + HMAC-SHA256，密钥 = SHA256(KeySalt)）</summary>
    public static string EncryptForTest(string plain)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(KeySalt));
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        byte[] cipher;
        using (var enc = aes.CreateEncryptor())
            cipher = enc.TransformFinalBlock(Encoding.UTF8.GetBytes(plain), 0, plain.Length);
        using var h = new HMACSHA256(aes.Key);
        var hmac = h.ComputeHash(cipher);
        var outBuf = new byte[16 + 32 + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, outBuf, 0, 16);
        Buffer.BlockCopy(hmac, 0, outBuf, 16, 32);
        Buffer.BlockCopy(cipher, 0, outBuf, 48, cipher.Length);
        return "AES-HMAC|" + Convert.ToBase64String(outBuf);
    }

    /// <summary>运行时解密（读注入字段）；失败返回空（安全降级 → 用户自填 key）</summary>
    public static string Decrypt() => Decrypt(Encrypted);

    /// <summary>解密指定密文；失败返回空（测试/篡改检测用）</summary>
    public static string Decrypt(string? encrypted)
    {
        try
        {
            if (string.IsNullOrEmpty(encrypted)) return "";
            if (!encrypted.StartsWith("AES-HMAC|", StringComparison.Ordinal)) return "";
            var raw = Convert.FromBase64String(encrypted["AES-HMAC|".Length..]);
            // 布局：iv(16) | hmac(32) | cipher(rest)
            if (raw.Length < 16 + 32 + 16) return "";
            var iv = raw[..16];
            var hmac = raw[16..48];
            var cipher = raw[48..];
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(KeySalt));
            // HMAC 校验（防篡改/防误写）
            using var h = new HMACSHA256(key);
            var expected = h.ComputeHash(cipher);
            if (!CryptographicOperations.FixedTimeEquals(hmac, expected)) return "";
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var dec = aes.CreateDecryptor();
            var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return ""; }
    }
}
