using Launcher.Core.Utils;
using Xunit;

namespace Launcher.Core.Tests;

public class SecretsTests
{
    [Fact]
    public void ProtectRead_RoundTrip_ReturnsOriginal()
    {
        const string plain = "cf-test-key-12345";
        var stored = Secrets.Protect(plain);
        Assert.StartsWith("dpapi:", stored);
        Assert.Equal(plain, Secrets.Read(stored));
    }

    [Fact]
    public void Protect_StoredValue_DoesNotContainPlaintext()
    {
        const string plain = "super-secret-key-abcdef";
        var stored = Secrets.Protect(plain);
        Assert.DoesNotContain(plain, stored);
        Assert.DoesNotContain(plain.ToUpperInvariant(), stored);
    }

    [Fact]
    public void Read_LegacyPlaintext_ReturnsAsIs()
    {
        // 旧版明文（无前缀）→ 原样返回，不丢数据
        Assert.Equal("legacy-key", Secrets.Read("legacy-key"));
    }

    [Fact]
    public void ProtectRead_Empty_ReturnsEmpty()
    {
        Assert.Equal("", Secrets.Protect(""));
        Assert.Equal("", Secrets.Read(""));
    }

    [Fact]
    public void Settings_SaveThenLoad_KeyRoundTripsAndFileIsEncrypted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lattice-settings-test-{Guid.NewGuid():N}.json");
        try
        {
            var s = new LauncherSettings { CurseForgeApiKey = "roundtrip-key-xyz" };
            s.Save(path);

            var onDisk = File.ReadAllText(path);
            Assert.Contains("dpapi:", onDisk);          // 落盘加密
            Assert.DoesNotContain("roundtrip-key-xyz", onDisk); // 文件不含明文

            var loaded = LauncherSettings.Load(path);
            Assert.Equal("roundtrip-key-xyz", loaded.CurseForgeApiKey); // 读回明文
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_SaveLegacyPlainFile_LoadThenSave_MigratesToEncrypted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lattice-settings-migrate-{Guid.NewGuid():N}.json");
        try
        {
            // 模拟旧版明文文件
            File.WriteAllText(path, """{"CurseForgeApiKey":"old-plain-key","VersionIsolation":false}""");
            var loaded = LauncherSettings.Load(path);
            Assert.Equal("old-plain-key", loaded.CurseForgeApiKey); // 明文可读

            loaded.Save(path); // 迁移：下次保存转加密
            var onDisk = File.ReadAllText(path);
            Assert.Contains("dpapi:", onDisk);
            Assert.DoesNotContain("old-plain-key", onDisk);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
