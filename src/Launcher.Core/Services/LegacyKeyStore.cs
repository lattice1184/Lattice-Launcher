using System.Security.Cryptography;

namespace Launcher.Core.Services;

/// <summary>
/// 旧版 KeyProxy 密文的读取（AL50 一次性迁移）：%AppData%\Launcher\keyproxy\key.bin 是
/// KeyProxy 时代 DPAPI 原始字节格式（非 Secrets 的 "dpapi:" base64 格式）。迁移完成后文件删除，
/// 本类不再有写入路径，仅保留读——缺失/损坏返回 null（视为未配置，用户重新填写）。
/// </summary>
public static class LegacyKeyStore
{
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Launcher", "keyproxy", "key.bin");

    /// <summary>读取并解密旧代理密文；文件缺失/换账户/损坏 → null</summary>
    public static string? ReadLegacyKey()
    {
        try
        {
            if (!File.Exists(DefaultFilePath)) return null;
            var enc = File.ReadAllBytes(DefaultFilePath);
            return System.Text.Encoding.UTF8.GetString(
                ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return null; // 换账户/损坏：视为未配置
        }
    }
}
