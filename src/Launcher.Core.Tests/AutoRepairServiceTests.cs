using Launcher.Core.Diagnostics;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Tests;

/// <summary>AL10.2：下载后完整性校验——修复不得"虚假成功"，缺失如实报告</summary>
public class AutoRepairServiceTests
{
    private static VersionJson BuildVersion()
    {
        var lib = new LibraryJson("net.fabricmc:fabric-loader:0.19.3", null, null, null, null, null, null, null);
        return new VersionJson("1.21.11", "release", "net.minecraft.client.main.Main",
            null, null, null, null, [lib], null, null, null, null);
    }

    [Fact]
    public async Task VerifyFiles_ReportsAllMissing_ThenEmptyAfterFill()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"verify-{Guid.NewGuid():N}");
        var version = BuildVersion();

        // 全新目录：client jar + library 都缺
        var report = await AutoRepairService.VerifyFilesAsync(version, gameDir);
        Assert.Equal(2, report.Missing);
        Assert.False(report.IsComplete);
        Assert.Contains(report.MissingFiles, p => p.EndsWith($"{Path.DirectorySeparatorChar}1.21.11.jar"));
        Assert.Contains(report.MissingFiles, p => p.Contains("fabric-loader-0.19.3.jar"));

        // 补齐后完整
        Directory.CreateDirectory(Path.Combine(gameDir, "versions", "1.21.11"));
        File.WriteAllText(Path.Combine(gameDir, "versions", "1.21.11", "1.21.11.jar"), "x");
        Directory.CreateDirectory(Path.Combine(gameDir, "libraries", "net", "fabricmc", "fabric-loader", "0.19.3"));
        File.WriteAllText(Path.Combine(gameDir, "libraries", "net", "fabricmc", "fabric-loader", "0.19.3", "fabric-loader-0.19.3.jar"), "x");

        var filled = await AutoRepairService.VerifyFilesAsync(version, gameDir);
        Assert.True(filled.IsComplete);
        Assert.Equal(2, filled.Present);
        Assert.Equal(2, filled.TotalExpected);
        Assert.True(filled.TotalBytes > 0);
        Assert.Contains("文件完整", filled.SummaryText);
    }

    /// <summary>AL11：VerifyFiles 按 OS 规则过滤——linux-only natives 库不会下载，不应误报缺失</summary>
    [Fact]
    public async Task VerifyFiles_SkipsOtherOsLibraries()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"verify-{Guid.NewGuid():N}");
        var libs = new List<LibraryJson>
        {
            new("net.fabricmc:fabric-loader:0.19.3", null, null, null, null, null, null, null),
            new("org.lwjgl:lwjgl-glfw:3.2.2", null, null, null, null,
                [new RuleJson("allow", new RuleOsInfo("linux", null, null), null)], null, null),
        };
        var version = new VersionJson("1.21.11", "release", "net.minecraft.client.main.Main",
            null, null, null, null, libs, null, null, null, null);

        var report = await AutoRepairService.VerifyFilesAsync(version, gameDir);
        Assert.Equal(2, report.Missing); // client jar + fabric-loader；linux-only 库被过滤
        Assert.DoesNotContain(report.MissingFiles, p => p.Contains("lwjgl-glfw-3.2.2"));
    }

    /// <summary>AL62 哈希质检：client jar 的 sha1 元数据 → 验证通过计数；内容不符 → 不通过</summary>
    [Fact]
    public async Task VerifyFiles_HashVerification_CountsMatches()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"verify-hash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gameDir, "versions", "1.21.11"));
        var jarPath = Path.Combine(gameDir, "versions", "1.21.11", "1.21.11.jar");
        File.WriteAllText(jarPath, "hello hash");
        var goodSha1 = System.Security.Cryptography.SHA1.HashData("hello hash"u8.ToArray());
        var badSha1 = System.Security.Cryptography.SHA1.HashData("other content"u8.ToArray());
        // 用真实 JSON 反序列化构造带 sha1 的版本（等价于官方 version json）
        var json = $$"""
            {"id":"1.21.11","mainClass":"net.minecraft.client.main.Main",
             "downloads":{"client":{"sha1":"{{Convert.ToHexStringLower(goodSha1)}}","size":11,"url":"https://x"} } }
            """;
        var withSha1 = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(json)!;

        var report = await AutoRepairService.VerifyFilesAsync(withSha1, gameDir, verifyHashes: true);
        Assert.True(report.IsComplete);
        Assert.Equal(1, report.VerifiedByHash); // 哈希匹配 → 通过

        var badJson = $$"""
            {"id":"1.21.11","mainClass":"net.minecraft.client.main.Main",
             "downloads":{"client":{"sha1":"{{Convert.ToHexStringLower(badSha1)}}","size":11,"url":"https://x"} } }
            """;
        var withBadSha1 = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(badJson)!;
        var bad = await AutoRepairService.VerifyFilesAsync(withBadSha1, gameDir, verifyHashes: true);
        Assert.Equal(0, bad.VerifiedByHash); // 哈希不符 → 0 通过（存在性仍完整）
    }
}
