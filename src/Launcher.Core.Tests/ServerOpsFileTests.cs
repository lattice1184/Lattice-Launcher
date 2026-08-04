using Launcher.Core.Server;

namespace Launcher.Core.Tests;

/// <summary>ops.json 解析（启动器图形化权限管理）</summary>
public class ServerOpsFileTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"ops-{Guid.NewGuid():N}");

    [Fact]
    public void Load_ParsesEntries()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "ops.json"),
                """[{"uuid":"a","name":"Steve","level":4,"bypassesPlayerLimit":true},{"uuid":"b","name":"Alex","level":2,"bypassesPlayerLimit":false}]""");
            var ops = ServerOpsFile.Load(dir);
            Assert.Equal(2, ops.Count);
            Assert.Contains(ops, o => o.Name == "Steve" && o.Level == 4);
            Assert.Contains(ops, o => o.Name == "Alex" && o.Level == 2);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var ops = ServerOpsFile.Load(TempDir());
        Assert.Empty(ops);
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsEmpty()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "ops.json"), "not-json{{");
            Assert.Empty(ServerOpsFile.Load(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
