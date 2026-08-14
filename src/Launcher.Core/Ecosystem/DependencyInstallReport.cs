namespace Launcher.Core.Ecosystem;

/// <summary>依赖安装结果报告</summary>
public sealed class DependencyInstallReport
{
    public List<InstalledDependency> Installed { get; } = [];
    public List<FailedDependency> Failed { get; } = [];
    public bool AllSucceeded => Failed.Count == 0;
}

public sealed record InstalledDependency(string ProjectId, string VersionId, string Path);

public sealed record FailedDependency(string ProjectId, string Reason);
