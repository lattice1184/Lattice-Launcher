using Launcher.Core.Server;

namespace Launcher.Core.Tests;

/// <summary>banned-players.json 解析（启动器图形化解封）</summary>
public class ServerBannedFileTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"banned-{Guid.NewGuid():N}");

    [Fact]
    public void Load_ParsesEntries()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "banned-players.json"),
                """[{"uuid":"a","name":"Steve","created":"2026-08-04 12:00:00 +0800","source":"Server","expires":"forever"},{"uuid":"b","name":"Alex","created":"2026-08-04 13:00:00 +0800","source":"Server","expires":"forever"}]""");
            var banned = ServerBannedFile.Load(dir);
            Assert.Equal(2, banned.Count);
            Assert.Contains(banned, b => b.Name == "Steve" && b.Expires == "forever");
            Assert.Contains(banned, b => b.Name == "Alex");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(ServerBannedFile.Load(TempDir()));
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsEmpty()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "banned-players.json"), "not-json{{");
            Assert.Empty(ServerBannedFile.Load(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
