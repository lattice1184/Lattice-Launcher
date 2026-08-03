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
            var zipPath = MrpackExporter.Export(versionDir, "1.21.1-fabric-0.16.9", outDir);
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
            var zipPath = MrpackExporter.Export(versionDir, "1.21.1", dir);

            using var zip = ZipFile.OpenRead(zipPath);
            using var sr = new StreamReader(zip.GetEntry("modrinth.index.json")!.Open());
            using var doc = JsonDocument.Parse(sr.ReadToEnd());
            var deps = doc.RootElement.GetProperty("dependencies");
            Assert.Equal("1.21.1", deps.GetProperty("minecraft").GetString());
            Assert.False(deps.TryGetProperty("fabric-loader", out _));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
