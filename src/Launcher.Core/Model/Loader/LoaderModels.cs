using System.Text.Json.Serialization;

namespace Launcher.Core.Model.Loader;

/// <summary>加载器类型（四家）</summary>
public enum LoaderKind { Fabric, Quilt, Forge, NeoForge }

/// <summary>加载器可用版本（UI 列表用）</summary>
public sealed record LoaderMetaVersion(string Version, bool IsStable);

/// <summary>安装计划：Fabric/Quilt 走 profile json 直装；Forge/NeoForge 走官方安装器进程</summary>
public sealed record LoaderInstallPlan(
    LoaderKind Kind,
    string McVersion,
    string LoaderVersion,
    string? ProfileJsonUrl,
    string? InstallerUrl,
    string? InstallerSha1,
    long? InstallerSize);

// ---------- Fabric / Quilt meta（两者结构一致） ----------

public sealed record FabricMetaEntry(
    [property: JsonPropertyName("loader")] FabricLoaderInfo? Loader,
    [property: JsonPropertyName("intermediary")] FabricArtifactInfo? Intermediary);

public sealed record FabricLoaderInfo(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("stable")] bool? Stable);

public sealed record FabricArtifactInfo(
    [property: JsonPropertyName("version")] string? Version);

// ---------- Forge promotions ----------

public sealed record ForgePromotions(
    [property: JsonPropertyName("promos")] Dictionary<string, string>? Promos);
