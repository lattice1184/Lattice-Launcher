using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>整合包导入：自家 manifest.json 解析 / 解压隔离实例 / 安装标记 / mrpack 降级提示</summary>
public class ModpackImporterTests
{
    private static string MakeZip(string dir, string manifestJson, params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "pack.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var m = zip.CreateEntry("manifest.json");
            using (var sw = new StreamWriter(m.Open(), Encoding.UTF8))
                sw.Write(manifestJson);

            foreach (var (path, content) in files)
            {
                var e = zip.CreateEntry(path);
                using (var sw = new StreamWriter(e.Open(), Encoding.UTF8))
                    sw.Write(content);
            }
        }
        return zipPath;
    }

    [Fact]
    public void Parse_OwnFormat_ReturnsInfo()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"imp-{Guid.NewGuid():N}");
        try
        {
            var zip = MakeZip(dir, """{"name":"整合测试","mcVersion":"1.21.1","loader":"fabric","fileCount":3}""");
            var info = ModpackImporter.Parse(zip, out var reason);
            Assert.True(info is not null, $"reason={reason}");
            Assert.Equal("整合测试", info!.VersionId);
            Assert.Equal("1.21.1", info.McVersion);
            Assert.Equal("fabric", info.Loader);
            Assert.Null(reason);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Parse_Mrpack_GivesDowngradeHint()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"imp-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, "pack.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("modrinth.index.json");
                using var sw = new StreamWriter(e.Open());
                sw.Write("""{"formatVersion":1,"dependencies":{"minecraft":"1.21.1"}}""");
            }
            var info = ModpackImporter.Parse(zip, out var reason);
            Assert.Null(info);
            Assert.Contains("mrpack", reason);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Parse_NoManifest_Unsupported()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"imp-{Guid.NewGuid():N}");
        try
        {
            var zip = MakeZip(dir, """{"nope":1}""");
            // 替换：写一个不含 manifest.json 的 zip
            File.Delete(zip);
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("mods/a.jar");
                using var sw = new StreamWriter(e.Open());
                sw.Write("x");
            }
            var info = ModpackImporter.Parse(zip, out var reason);
            Assert.Null(info);
            Assert.Contains("不支持", reason);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Import_ExtractsIsolatedInstance_AndMarks()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"imp-{Guid.NewGuid():N}");
        try
        {
            var gameDir = Path.Combine(dir, "game");
            var zip = MakeZip(dir, """{"name":"pack-a","mcVersion":"1.21.1","loader":null,"fileCount":3}""",
                ("mods/example.jar", "JAR"),
                ("config/options.txt", "opt"),
                ("saves/世界/level.dat", "dat"));

            ModpackImporter.Import(zip, gameDir, CancellationToken.None);

            var vdir = Path.Combine(gameDir, "versions", "pack-a");
            Assert.True(File.Exists(Path.Combine(vdir, "mods", "example.jar")));
            Assert.True(File.Exists(Path.Combine(vdir, "config", "options.txt")));
            Assert.True(File.Exists(Path.Combine(vdir, "saves", "世界", "level.dat")));
            Assert.False(File.Exists(Path.Combine(vdir, "manifest.json"))); // 清单不入库
            Assert.True(InstallMarker.IsMarked(gameDir, "pack-a"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Import_PathTraversal_Blocked()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"imp-{Guid.NewGuid():N}");
        try
        {
            var gameDir = Path.Combine(dir, "game");
            var zip = MakeZip(dir, """{"name":"pack-b"}""",
                ("../evil.txt", "hack"));

            ModpackImporter.Import(zip, gameDir, CancellationToken.None);
            Assert.False(File.Exists(Path.Combine(dir, "evil.txt"))); // 未逃出
            Assert.True(InstallMarker.IsMarked(gameDir, "pack-b"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
