using System.Text.Json.Serialization;

namespace Launcher.Core.Model.Modrinth;

/// <summary>项目的一个发布版本（GET /v2/project/{id}/version）</summary>
public sealed record ModrinthVersion(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("project_id")] string ProjectId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version_number")] string VersionNumber,
    [property: JsonPropertyName("game_versions")] List<string>? GameVersions,
    [property: JsonPropertyName("loaders")] List<string>? Loaders,
    [property: JsonPropertyName("files")] List<ModrinthVersionFile>? Files,
    [property: JsonPropertyName("dependencies")] List<ModrinthDependency>? Dependencies,
    [property: JsonPropertyName("changelog")] string? Changelog,
    [property: JsonPropertyName("downloads")] long Downloads,
    [property: JsonPropertyName("version_type")] string? VersionType,
    [property: JsonPropertyName("featured")] bool? Featured,
    [property: JsonPropertyName("date_published")] DateTime DatePublished);

/// <summary>版本文件（下载目标）</summary>
public sealed record ModrinthVersionFile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("filename")] string FileName,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("primary")] bool Primary,
    [property: JsonPropertyName("hashes")] ModrinthHashes? Hashes);

public sealed record ModrinthHashes(
    [property: JsonPropertyName("sha1")] string? Sha1,
    [property: JsonPropertyName("sha512")] string? Sha512);

/// <summary>依赖项（MVP 仅占位解析，不处理依赖链）</summary>
public sealed record ModrinthDependency(
    [property: JsonPropertyName("version_id")] string? VersionId,
    [property: JsonPropertyName("project_id")] string? ProjectId,
    [property: JsonPropertyName("file_name")] string? FileName,
    [property: JsonPropertyName("dependency_type")] string? DependencyType);
