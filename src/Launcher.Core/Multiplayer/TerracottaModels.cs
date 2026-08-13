using System.Text.Json.Serialization;

namespace Launcher.Core.Multiplayer;

/// <summary>已安装的陶瓦模块（terracotta.exe + 运行库 + manifest）——provisioning 专用
/// （联机会话模型 Snapshot/Player/State/StopReason/Failure/Exception 已通用化到 MultiplayerModels.cs，
/// 8-14 EasyTier 第二联机共用）</summary>
public sealed record TerracottaModule(string Version, string Architecture, string Directory, string ExePath);

/// <summary>陶瓦进度回显（stage：terracotta-download / terracotta-extract / terracotta-ready）</summary>
public sealed record TerracottaProvisionProgress(string Stage, int Percent);
