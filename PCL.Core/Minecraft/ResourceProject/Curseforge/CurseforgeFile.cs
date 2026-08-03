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
    CurseforgeHashes hashes,
    string downloadUrl = "",
    long fileLength = 0,
    List<string>? gameVersions = null,
    List<CurseforgeFileDependency>? dependencies = null);