namespace Launcher.Core.Launch;

/// <summary>
/// 加载器版本依赖的父版本未安装（AL29 C2——与「客户端文件缺失」区分：修复目标不同，
/// 前者需安装原版父版本，后者重下子版本。HomeViewModel 据此不误显修复按钮、不自动重下）。
/// </summary>
public sealed class ParentVersionMissingException : InvalidOperationException
{
    public ParentVersionMissingException(string message) : base(message) { }
}
