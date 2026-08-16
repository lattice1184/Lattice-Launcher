namespace Launcher.Core.Services;

/// <summary>
/// 内置 CF key 槽位（8-14）：值来自 BundledCfKeyGen（发布.ps1 构建注入覆盖）。
/// 对齐 PCL/HMCL 构建注入——key 不进源码仓库，GitHub 上永远是空占位；
/// 本地发布时从环境变量 LATTICE_CF_KEY 注入混淆值。Base64 + 字符位移(+7) 不明文。
/// 用户设置页自填 key 优先于本槽位（ResolveApiKey 顺序保证），两者隔离互不影响。
/// </summary>
internal static class BundledCfKey
{
    private const int Shift = 7;

    public static string? Decode() => Decode(BundledCfKeyGen.Obfuscated);

    public static string? Decode(string? obfuscated)
    {
        if (string.IsNullOrEmpty(obfuscated)) return null;
        try
        {
            var bytes = Convert.FromBase64String(obfuscated);
            var chars = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++) chars[i] = (char)(bytes[i] - Shift);
            return new string(chars);
        }
        catch { return null; }
    }

    /// <summary>填 key 用：明文 → 混淆串。Encode→Decode 往返由测试保证。</summary>
    public static string? EncodeForBundling(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = new byte[plain.Length];
        for (var i = 0; i < plain.Length; i++) bytes[i] = (byte)(plain[i] + Shift);
        return Convert.ToBase64String(bytes);
    }
}
