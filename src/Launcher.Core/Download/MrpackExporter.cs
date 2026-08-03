using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Launcher.Core.Download;

/// <summary>
/// 整合包导出为 Modrinth mrpack 格式（modrinth.index.json + overrides/）：
/// 标准格式可被 PCL/HMCL/Modrinth App 导入。files 记录 mods 目录 jar 的 sha1/sha512。
/// </summary>
public static class MrpackExporter
{
    /// <summary>导出 → .mrpack 文件路径（versionDir 为隔离实例根：mods/saves/config…）</summary>
    public static string Export(string versionDir, string versionId, string outDir, string? mcVersion = null, string? loader = null)
    {
        Directory.CreateDirectory(outDir);
        var zipPath = Path.Combine(outDir, $"{versionId}-整合包.mrpack");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // 1. modrinth.index.json
        var modsDir = Path.Combine(versionDir, "mods");
        var files = new List<object>();
        if (Directory.Exists(modsDir))
        {
            foreach (var jar in Directory.EnumerateFiles(modsDir, "*.jar"))
            {
                var rel = Path.Combine("mods", Path.GetFileName(jar)).Replace('\\', '/');
                var sha1 = HashFile(jar, SHA1.HashData);
                var sha512 = HashFile(jar, SHA512.HashData);
                files.Add(new
                {
                    path = rel,
                    hashes = new { sha1, sha512 },
                    env = new { client = "required", server = "unsupported" },
                    downloads = Array.Empty<string>(),
                    fileSize = new FileInfo(jar).Length,
                });
            }
        }

        var index = new
        {
            formatVersion = 1,
            game = "minecraft",
            versionId = versionId,
            name = versionId,
            summary = $"由 YanKa Launcher 导出的整合包 {versionId}",
            files,
            dependencies = BuildDependencies(mcVersion, loader, versionDir),
        };
        var entry = zip.CreateEntry("modrinth.index.json");
        using (var sw = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            sw.Write(JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));

        // 2. overrides/：mods（重复的已由 files 引用，overrides 放配置/存档/资源）
        foreach (var sub in new[] { "config", "saves", "resourcepacks", "shaderpacks", "options.txt" })
        {
            var src = Path.Combine(versionDir, sub);
            if (Directory.Exists(src))
                AddDir(zip, src, Path.Combine("overrides", sub));
            else if (File.Exists(src))
                AddFile(zip, src, Path.Combine("overrides", sub));
        }

        return zipPath;
    }

    private static Dictionary<string, string> BuildDependencies(string? mcVersion, string? loader, string versionDir)
    {
        // 从版本 id 解析（1.21.1-fabric-0.16.9 → minecraft=1.21.1, fabric-loader）
        var id = Path.GetFileName(versionDir);
        var deps = new Dictionary<string, string>();
        var mc = mcVersion ?? System.Text.RegularExpressions.Regex.Match(id, @"^\d+\.\d+(\.\d+)?").Value;
        if (mc.Length > 0) deps["minecraft"] = mc;
        var loaderName = loader ?? (id.ToLowerInvariant() switch
        {
            var l when l.Contains("neoforge") => "neoforge",
            var l when l.Contains("fabric") => "fabric-loader",
            var l when l.Contains("quilt") => "quilt-loader",
            var l when l.Contains("forge") => "forge",
            _ => "",
        });
        if (loaderName.Length > 0) deps[loaderName] = "*";
        return deps;
    }

    private static string HashFile(string path, Func<byte[], byte[]> algo)
        => Convert.ToHexStringLower(algo(File.ReadAllBytes(path)));

    private static void AddDir(ZipArchive zip, string srcDir, string destPrefix)
    {
        foreach (var f in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir, f);
            AddFile(zip, f, Path.Combine(destPrefix, rel));
        }
    }

    private static void AddFile(ZipArchive zip, string src, string dest)
    {
        var entry = zip.CreateEntry(dest.Replace('\\', '/'));
        using var input = File.OpenRead(src);
        using var output = entry.Open();
        input.CopyTo(output);
    }
}
