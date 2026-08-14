using System.Text.Json.Serialization;

namespace Launcher.Core.Model.Mojang;

/// <summary>
/// 依赖库项。名称按 Gradle 坐标解析（group:artifact:version[:classifier]），路径规则见 MavenPath
/// </summary>
public sealed record LibraryJson(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("sha1")] string? Sha1,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("downloads")] LibraryDownloads? Downloads,
    [property: JsonPropertyName("rules")] List<RuleJson>? Rules,
    [property: JsonPropertyName("natives")] Dictionary<string, string>? Natives,
    [property: JsonPropertyName("extract")] ExtractRule? Extract);

public sealed record LibraryDownloads(
    [property: JsonPropertyName("artifact")] DownloadFileInfo? Artifact,
    [property: JsonPropertyName("classifiers")] Dictionary<string, DownloadFileInfo>? Classifiers);

public sealed record ExtractRule(
    [property: JsonPropertyName("exclude")] List<string>? Exclude);

/// <summary>
/// 规则：action = allow / disallow；os.name ∈ windows/linux/osx；os.version 为正则；arch ∈ x86/x64/arm64
/// </summary>
public sealed record RuleJson(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("os")] RuleOsInfo? Os,
    [property: JsonPropertyName("features")] Dictionary<string, bool>? Features);

public sealed record RuleOsInfo(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("arch")] string? Arch);
