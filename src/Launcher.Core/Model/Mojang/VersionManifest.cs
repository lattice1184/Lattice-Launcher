using System.Text.Json.Serialization;

namespace Launcher.Core.Model.Mojang;

/// <summary>
/// Mojang 官方版本清单（version_manifest_v2.json）
/// </summary>
public sealed record VersionManifest(
    [property: JsonPropertyName("latest")] LatestVersions Latest,
    [property: JsonPropertyName("versions")] List<VersionEntry> Versions);

public sealed record LatestVersions(
    [property: JsonPropertyName("release")] string Release,
    [property: JsonPropertyName("snapshot")] string Snapshot);

public sealed record VersionEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("time")] DateTime Time,
    [property: JsonPropertyName("releaseTime")] DateTime ReleaseTime,
    [property: JsonPropertyName("sha1")] string? Sha1,
    [property: JsonPropertyName("complianceLevel")] int? ComplianceLevel);
