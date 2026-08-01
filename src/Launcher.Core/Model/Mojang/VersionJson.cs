using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launcher.Core.Model.Mojang;

/// <summary>
/// 单个版本的核心 JSON（version.json），覆盖 1.7.10 ~ 最新版全部字段形态
/// </summary>
public sealed record VersionJson(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("mainClass")] string? MainClass,
    [property: JsonPropertyName("assets")] string? Assets,
    [property: JsonPropertyName("assetIndex")] AssetIndexInfo? AssetIndex,
    [property: JsonPropertyName("arguments")] ArgumentsInfo? Arguments,
    [property: JsonPropertyName("minecraftArguments")] string? MinecraftArguments,
    [property: JsonPropertyName("libraries")] List<LibraryJson>? Libraries,
    [property: JsonPropertyName("downloads")] DownloadsInfo? Downloads,
    [property: JsonPropertyName("javaVersion")] JavaVersionInfo? JavaVersion,
    [property: JsonPropertyName("logging")] LoggingInfo? Logging);

public sealed record AssetIndexInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha1")] string? Sha1,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("totalSize")] long? TotalSize);

/// <summary>
/// arguments 中 game/jvm 是混合数组：字符串（直接参数）或 { rules, value } 对象
/// </summary>
public sealed record ArgumentsInfo(
    [property: JsonPropertyName("game")] List<JsonElement>? Game,
    [property: JsonPropertyName("jvm")] List<JsonElement>? Jvm);

public sealed record DownloadsInfo(
    [property: JsonPropertyName("client")] DownloadFileInfo? Client,
    [property: JsonPropertyName("server")] DownloadFileInfo? Server,
    [property: JsonPropertyName("client_mappings")] DownloadFileInfo? ClientMappings);

public sealed record DownloadFileInfo(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha1")] string? Sha1,
    [property: JsonPropertyName("size")] long? Size);

public sealed record JavaVersionInfo(
    [property: JsonPropertyName("majorVersion")] int MajorVersion,
    [property: JsonPropertyName("component")] string? Component);

public sealed record LoggingInfo(
    [property: JsonPropertyName("client")] LoggingClientInfo? Client);

public sealed record LoggingClientInfo(
    [property: JsonPropertyName("argument")] string? Argument,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("file")] DownloadFileInfo? File);
