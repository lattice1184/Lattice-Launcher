namespace Launcher.Core.Launch;

/// <summary>
/// PNG 头解析（8-16 批次 51：Core 层无 Avalonia，不能 Bitmap 校验尺寸——手写魔数 + IHDR）。
/// 校验魔数并读 IHDR 宽高；非 PNG / 损坏返回 null。
/// </summary>
public static class SkinPngHeader
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>解析 PNG 头：非 PNG / 数据不足 / 无 IHDR 返回 null；否则 (宽, 高)</summary>
    public static (int Width, int Height)? TryParse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24) return null; // 签名 8 + 长度 4 + "IHDR" 4 + 宽高 8
        for (var i = 0; i < Signature.Length; i++)
            if (bytes[i] != Signature[i]) return null; // 非 PNG 魔数
        // 第 9-12 字节 = IHDR chunk 长度（应为 13），第 13-16 = "IHDR"
        if (bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
            return null;
        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        if (width <= 0 || height <= 0) return null;
        return (width, height);
    }
}
