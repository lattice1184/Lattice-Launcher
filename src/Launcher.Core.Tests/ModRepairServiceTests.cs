using Launcher.Core.Diagnostics;

namespace Launcher.Core.Tests;

/// <summary>模组缺失自愈（AL57）：日志解析提取缺失前置（Fabric/Forge 报错样本）+ 空日志短路</summary>
public class ModRepairServiceTests
{
    private static (string GameDir, string InstanceId) TempInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mrep-{Guid.NewGuid():N}");
        var instance = Path.Combine(root, "versions", "1.21.1-Fabric");
        Directory.CreateDirectory(Path.Combine(instance, "logs"));
        return (root, "1.21.1-Fabric");
    }

    private static void WriteLog(string gameDir, string instanceId, string content)
    {
        File.WriteAllText(Path.Combine(gameDir, "versions", instanceId, "logs", "latest.log"), content);
    }

    [Fact]
    public void Fabric_MissingMods_ExtractsIds()
    {
        var (gameDir, id) = TempInstance();
        try
        {
            WriteLog(gameDir, id, """
                [main/ERROR]: Missing mods:
                [main/ERROR]: 	- fabric-api
                [main/ERROR]: 	- sodium
                """);
            var ids = ModRepairService.ScanInstanceLogs(gameDir, id);
            Assert.Contains("fabric-api", ids);
            Assert.Contains("sodium", ids);
        }
        finally { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); }
    }

    [Fact]
    public void Fabric_CouldNotLoad_ExtractsMissingDep()
    {
        var (gameDir, id) = TempInstance();
        try
        {
            WriteLog(gameDir, id, "Couldn't load mod sodium because it is missing fabric-api.");
            var ids = ModRepairService.ScanInstanceLogs(gameDir, id);
            Assert.Contains("fabric-api", ids);
        }
        finally { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); }
    }

    [Fact]
    public void Forge_RequiresQuoted_ExtractsModId()
    {
        var (gameDir, id) = TempInstance();
        try
        {
            WriteLog(gameDir, id, """
                The mod "Some Mod" requires mod 'bookshelf' (or newer)
                The mod "Another" requires mod 'cloth-config'
                """);
            var ids = ModRepairService.ScanInstanceLogs(gameDir, id);
            Assert.Contains("bookshelf", ids);
            Assert.Contains("cloth-config", ids);
        }
        finally { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); }
    }

    [Fact]
    public void RequiresUnquotedWords_NotExtracted()
    {
        var (gameDir, id) = TempInstance();
        try
        {
            // 未引号的 requires（java/api 等修饰词）不误报；引号包裹才算模组 id
            WriteLog(gameDir, id, "The mod requires API and Java 17 to run. requires mod 'real-mod'");
            var ids = ModRepairService.ScanInstanceLogs(gameDir, id);
            Assert.DoesNotContain("api", ids);
            Assert.DoesNotContain("java", ids);
            Assert.Contains("real-mod", ids);
        }
        finally { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); }
    }

    [Fact]
    public void NoLog_ReturnsEmpty()
    {
        var (gameDir, id) = TempInstance();
        try
        {
            Assert.Empty(ModRepairService.ScanInstanceLogs(gameDir, id)); // 无日志不误报
        }
        finally { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); }
    }
}
