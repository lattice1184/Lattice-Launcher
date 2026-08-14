namespace Launcher.App.Services;

/// <summary>导出整合包设置（PCL 式：内容勾选 + 输出位置 + 包信息）</summary>
public sealed record ExportSettings(
    bool IncludeMods, bool IncludeSaves, bool IncludeConfig,
    bool IncludeResourcepacks, bool IncludeShaders, bool IncludeOptions,
    string OutputDir, string Name, string Description)
{
    /// <summary>是否勾选了任何内容</summary>
    public bool HasAnyContent => IncludeMods || IncludeSaves || IncludeConfig
        || IncludeResourcepacks || IncludeShaders || IncludeOptions;
}
