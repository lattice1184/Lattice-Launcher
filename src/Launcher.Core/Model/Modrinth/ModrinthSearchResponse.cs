using System.Text.Json.Serialization;

namespace Launcher.Core.Model.Modrinth;

/// <summary>Modrinth 搜索响应（GET /v2/search）</summary>
public sealed record ModrinthSearchResponse(
    [property: JsonPropertyName("hits")] List<ModrinthSearchHit>? Hits,
    [property: JsonPropertyName("total_hits")] int? TotalHits,
    [property: JsonPropertyName("offset")] int? Offset,
    [property: JsonPropertyName("limit")] int? Limit);

/// <summary>搜索命中项（卡片数据源）</summary>
public sealed record ModrinthSearchHit(
    [property: JsonPropertyName("project_id")] string ProjectId,
    [property: JsonPropertyName("project_type")] string ProjectType,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("categories")] List<string>? Categories,
    [property: JsonPropertyName("display_categories")] List<string>? DisplayCategories,
    [property: JsonPropertyName("versions")] List<string>? Versions,
    [property: JsonPropertyName("icon_url")] string? IconUrl,
    [property: JsonPropertyName("downloads")] long Downloads,
    [property: JsonPropertyName("follows")] long Follows,
    [property: JsonPropertyName("date_created")] DateTime DateCreated,
    [property: JsonPropertyName("date_modified")] DateTime DateModified,
    [property: JsonPropertyName("latest_version")] string? LatestVersion);
