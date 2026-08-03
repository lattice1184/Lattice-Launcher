using System.IO.Compression;
using System.Text.Json;

namespace Launcher.Core.Download;

/// <summary>整合包导入信息（来自导出时生成的 manifest.json）</summary>
public sealed record ModpackImportInfo(string VersionId, string McVersion, string? Loader, int FileCount);

/// <summary>
/// 整合包导入：解析自家导出的 zip（manifest.json + mods/saves/config 等），
/// 解压为隔离版本实例（InstallDir/versions/{id}）并写安装标记。
/// mrpack（modrinth.index.json）识别后给出降级提示（变体暂不支持）。
/// </summary>
public sealed class ModpackImporter
{
    /// <summary>整合包清单（公开类型：System.Text.Json 反射要求 public）</summary>
    public sealed class ManifestJson
    {
        public string? Name { get; set; }
        public string? McVersion { get; set; }
        public string? Loader { get; set; }
        public int? FileCount { get; set; }
    }

    /// <summary>解析 zip → 导入信息；不支持的格式返回 null 并给出原因</summary>
    public static ModpackImportInfo? Parse(string zipPath, out string? unsupportedReason)
    {
        unsupportedReason = null;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var manifest = zip.GetEntry("manifest.json");
            if (manifest is null)
            {
                unsupportedReason = zip.GetEntry("modrinth.index.json") is not null
                    ? "这是 Modrinth mrpack 格式（暂不支持导入；可在【整合包】tab 在线搜索安装）"
                    : "未找到 manifest.json（不支持该整合包格式）";
                return null;
            }
            using var sr = new StreamReader(manifest.Open());
            // 大小写不敏感：自家导出用匿名小写属性（name/mcVersion…），反序列化必须匹配
            var m = JsonSerializer.Deserialize<ManifestJson>(sr.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (m is null || string.IsNullOrEmpty(m.Name))
            {
                unsupportedReason = "manifest.json 解析失败";
                return null;
            }
            return new ModpackImportInfo(m.Name, m.McVersion ?? "", m.Loader, m.FileCount ?? 0);
        }
        catch (Exception ex)
        {
            unsupportedReason = $"读取失败: {ex.Message}";
            return null;
        }
    }

    /// <summary>版本 id 清洗：非法文件名字符替换为下划线（防路径注入/目录穿越/非法目录名）</summary>
    public static string SafeId(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch == '\\' || ch == '/' || invalid.Contains(ch)) sb.Append('_');
            else sb.Append(ch);
        }
        var id = sb.ToString().Trim();
        return string.IsNullOrEmpty(id) ? "modpack" : id;
    }

    /// <summary>解压为隔离版本实例并写安装标记（zip 内 manifest.json 跳过；防目录穿越）</summary>
    public static void Import(string zipPath, string gameDir, CancellationToken ct)
    {
        var info = Parse(zipPath, out _) ?? throw new InvalidDataException("不支持的整合包格式");
        var versionDir = Path.Combine(gameDir, "versions", SafeId(info.VersionId));
        Directory.CreateDirectory(versionDir);

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName == "manifest.json") continue;
            if (entry.FullName.EndsWith('/')) continue; // 目录条目由 ExtractToFile 隐式创建

            var dest = Path.GetFullPath(Path.Combine(versionDir, entry.FullName));
            if (!dest.StartsWith(versionDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue; // 目录穿越防护
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }

        InstallMarker.Mark(gameDir, info.VersionId);
    }
}
