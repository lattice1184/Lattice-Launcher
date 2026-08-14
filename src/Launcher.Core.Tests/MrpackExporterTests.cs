using System.IO.Compression;
using System.Text.Json;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>mrpack 导出：modrinth.index.json 结构 / files 哈希 / overrides</summary>
public class MrpackExporterTests
{
    [Fact]
    public void Export_CreatesIndexWithFilesAndOverrides()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mrpack-{Guid.NewGuid():N}");
        try
        {
            var versionDir = Path.Combine(dir, "versions", "1.21.1-fabric-0.16.9");
            Directory.CreateDirectory(Path.Combine(versionDir, "mods"));
            Directory.CreateDirectory(Path.Combine(versionDir, "config"));
            File.WriteAllBytes(Path.Combine(versionDir, "mods", "sodium.jar"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllText(Path.Combine(versionDir, "config", "x.toml"), "a=1");

            var outDir = Path.Combine(dir, "out");
            var opts = new MrpackExporter.ExportOptions(true, true, true, true, true, true, "1.21.1-fabric-0.16.9", "");
            var zipPath = MrpackExporter.Export(versionDir, opts, outDir);
            Assert.True(File.Exists(zipPath));
            Assert.EndsWith(".mrpack", zipPath);

            using var zip = ZipFile.OpenRead(zipPath);
            Assert.NotNull(zip.GetEntry("modrinth.index.json"));
            Assert.NotNull(zip.GetEntry("overrides/config/x.toml"));

            using var sr = new StreamReader(zip.GetEntry("modrinth.index.json")!.Open());
            using var doc = JsonDocument.Parse(sr.ReadToEnd());
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
            Assert.Equal("minecraft", root.GetProperty("game").GetString());
            Assert.Equal("1.21.1", root.GetProperty("dependencies").GetProperty("minecraft").GetString());
            Assert.Equal("*", root.GetProperty("dependencies").GetProperty("fabric-loader").GetString());

            var files = root.GetProperty("files").EnumerateArray().ToList();
            Assert.Single(files);
            Assert.Equal("mods/sodium.jar", files[0].GetProperty("path").GetString());
            Assert.Equal(40, files[0].GetProperty("hashes").GetProperty("sha1").GetString()!.Length); // SHA1 = 20 字节 = 40 hex
            Assert.Equal(128, files[0].GetProperty("hashes").GetProperty("sha512").GetString()!.Length);
            Assert.Equal(4, files[0].GetProperty("fileSize").GetInt64());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Export_Vanilla_NoLoaderDependency()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mrpack-{Guid.NewGuid():N}");
        try
        {
            var versionDir = Path.Combine(dir, "versions", "1.21.1");
            Directory.CreateDirectory(versionDir);
            var opts = new MrpackExporter.ExportOptions(true, true, true, true, true, true, "1.21.1", "");
            var zipPath = MrpackExporter.Export(versionDir, opts, dir);

            using var zip = ZipFile.OpenRead(zipPath);
            using var sr = new StreamReader(zip.GetEntry("modrinth.index.json")!.Open());
            using var doc = JsonDocument.Parse(sr.ReadToEnd());
            var deps = doc.RootElement.GetProperty("dependencies");
            Assert.Equal("1.21.1", deps.GetProperty("minecraft").GetString());
            Assert.False(deps.TryGetProperty("fabric-loader", out _));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Export_ModsOnly_OverridesHasNoConfigSaves()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mrpack-{Guid.NewGuid():N}");
        try
        {
            var versionDir = Path.Combine(dir, "versions", "1.21.1");
            Directory.CreateDirectory(Path.Combine(versionDir, "mods"));
            Directory.CreateDirectory(Path.Combine(versionDir, "config"));
            Directory.CreateDirectory(Path.Combine(versionDir, "saves"));
            File.WriteAllBytes(Path.Combine(versionDir, "mods", "a.jar"), new byte[] { 1 });

            var opts = new MrpackExporter.ExportOptions(
                IncludeMods: true, IncludeSaves: false, IncludeConfig: false,
                IncludeResourcepacks: false, IncludeShaders: false, IncludeOptions: false,
                Name: "mods-only", Description: "test");
            var zipPath = MrpackExporter.Export(versionDir, opts, dir);

            using var zip = ZipFile.OpenRead(zipPath);
            // mrpack 规范：mods 只进 files（downloads 引用），不进 overrides
            Assert.Null(zip.GetEntry("overrides/mods"));             // 模组由 files 引用
            Assert.Null(zip.GetEntry("overrides/config"));           // 未勾选 → 无 config
            Assert.Null(zip.GetEntry("overrides/saves"));            // 未勾选 → 无 saves

            using var sr = new StreamReader(zip.GetEntry("modrinth.index.json")!.Open());
            using var doc = JsonDocument.Parse(sr.ReadToEnd());
            Assert.Single(doc.RootElement.GetProperty("files").EnumerateArray());
            Assert.Equal("mods-only", doc.RootElement.GetProperty("name").GetString());
            Assert.Equal("test", doc.RootElement.GetProperty("summary").GetString());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
