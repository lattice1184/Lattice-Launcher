using System;
using System.Collections.Generic;

namespace PCL.Core.Minecraft.ResourceProject.Curseforge;

[Serializable]
public record CurseforgeFile(
    int id,
    int gameId,
    int modId,
    bool isAvailable,
    string displayName,
    string fileName,
    int releaseType,
    int fileStatus,
    List<CurseforgeHashes>? hashes = null, // 8-22 修复：CF 实际返回数组 [{"value","algo"}]，旧单对象类型导致
                                             // 非空 files 列表 Deserialize 必抛 JsonException → UI 误报「响应格式异常」
    string downloadUrl = "",
    long fileLength = 0,
    List<string>? gameVersions = null,
    List<CurseforgeFileDependency>? dependencies = null);