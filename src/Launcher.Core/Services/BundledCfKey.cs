namespace Launcher.Core.Services;

/// <summary>
/// 内置 CF key 槽位（8-14）：值来自 BundledCfKeyGen（发布.ps1 构建注入覆盖）。
/// 对齐 PCL/HMCL 构建注入——key 不进源码仓库，GitHub 上永远是空占位；
/// 8-19 升级：注入值 = AES-256-CBC + HMAC-SHA256 密文（AES-HMAC| 前缀，与 PCL2 同思路），
/// 密钥由内嵌常量派生——直接 grep 二进制拿不到密钥与明文（旧版 +7 移位 30 秒可逆）。
/// 用户设置页自填 key 优先于本槽位（ResolveApiKey 顺序保证），两者隔离互不影响。
/// </summary>
internal static class BundledCfKey
{
    /// <summary>解密内置 key；无内置值/解密失败 → null（安全降级：用户自填 key 兜底）</summary>
    public static string? Decode() => BundledCfKeyGen.Decrypt() is { Length: > 0 } v ? v : null;

    /// <summary>解密指定密文（测试/篡改检测用）；失败 → null</summary>
    public static string? Decode(string? encrypted)
        => BundledCfKeyGen.Decrypt(encrypted) is { Length: > 0 } v ? v : null;

    /// <summary>填 key 用：明文 → AES-HMAC 密文。Encode→Decode 往返由测试保证
    /// （与发布.ps1 Inject-BundledCfKey 的加密实现一一对应）</summary>
    public static string? EncodeForBundling(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        return BundledCfKeyGen.EncryptForTest(plain);
    }
}
