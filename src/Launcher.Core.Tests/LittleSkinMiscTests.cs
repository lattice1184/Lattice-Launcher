using System.Text;
using Launcher.Core.Account;
using Launcher.Core.Launch;

namespace Launcher.Core.Tests;

/// <summary>8-16 批次 51：token 存储 + PNG 头解析 + 皮肤强写（皮肤库配套基础件）</summary>
public class LittleSkinMiscTests
{
    // ---------- TokenStore ----------

    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), "lattice-ls-token-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void TokenStore_RoundTrip_NoPlaintextOnDisk()
    {
        var path = TempPath();
        try
        {
            var store = new LittleSkinTokenStore(path);
            store.Save(new LittleSkinOAuth.TokenPair("at-secret", "rt-secret", 3600));
            var loaded = store.Load();
            Assert.NotNull(loaded);
            Assert.Equal("at-secret", loaded!.AccessToken);
            Assert.Equal("rt-secret", loaded.RefreshToken);
            Assert.Equal(3600, loaded.ExpiresInSec);

            var json = File.ReadAllText(path);
            Assert.DoesNotContain("at-secret", json); // 落盘无明文
            Assert.Contains("dpapi:", json);          // DPAPI 前缀
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TokenStore_CorruptFile_ReturnsNull_NotThrow()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{{not-json");
            var store = new LittleSkinTokenStore(path);
            Assert.Null(store.Load());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TokenStore_Clear_RemovesFile()
    {
        var path = TempPath();
        try
        {
            var store = new LittleSkinTokenStore(path);
            store.Save(new LittleSkinOAuth.TokenPair("a", "b", 1));
            Assert.True(File.Exists(path));
            store.Clear();
            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---------- PNG 头 ----------

    private static byte[] PngWithSize(int w, int h)
    {
        var bytes = new byte[24];
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(sig, bytes, 8);
        // 长度 13（bytes 8-11 默认 0 不对——补一下）；IHDR 标识；宽高大端
        bytes[8] = 0; bytes[9] = 0; bytes[10] = 0; bytes[11] = 13;
        bytes[12] = (byte)'I'; bytes[13] = (byte)'H'; bytes[14] = (byte)'D'; bytes[15] = (byte)'R';
        bytes[16] = (byte)(w >> 24); bytes[17] = (byte)(w >> 16); bytes[18] = (byte)(w >> 8); bytes[19] = (byte)w;
        bytes[20] = (byte)(h >> 24); bytes[21] = (byte)(h >> 16); bytes[22] = (byte)(h >> 8); bytes[23] = (byte)h;
        return bytes;
    }

    [Theory]
    [InlineData(64, 64)]
    [InlineData(64, 32)]
    public void PngHeader_ParsesValid(int w, int h)
    {
        var (pw, ph) = SkinPngHeader.TryParse(PngWithSize(w, h))!.Value;
        Assert.Equal((w, h), (pw, ph));
    }

    [Fact]
    public void PngHeader_NotPng_ReturnsNull()
    {
        Assert.Null(SkinPngHeader.TryParse(Encoding.ASCII.GetBytes("hello world this is not png at all")));
        Assert.Null(SkinPngHeader.TryParse(ReadOnlySpan<byte>.Empty));
        Assert.Null(SkinPngHeader.TryParse(new byte[10])); // 数据不足
    }

    // ---------- 皮肤强写 ----------

    [Fact]
    public void FileWriter_Overwrites_AndClearsReadonly()
    {
        var path = Path.Combine(Path.GetTempPath(), "lattice-skin-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, PngWithSize(64, 64));
            File.SetAttributes(path, FileAttributes.ReadOnly); // 只读也强制覆盖
            SkinFileWriter.ForceWrite(path, PngWithSize(64, 32));
            var written = File.ReadAllBytes(path);
            Assert.Equal(64, SkinPngHeader.TryParse(written)!.Value.Width);
            Assert.Equal(32, SkinPngHeader.TryParse(written)!.Value.Height);
            Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly)); // 只读已清除
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
