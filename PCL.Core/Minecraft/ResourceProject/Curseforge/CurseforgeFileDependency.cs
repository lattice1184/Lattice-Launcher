using System;

namespace PCL.Core.Minecraft.ResourceProject.Curseforge;

[Serializable]
public record CurseforgeFileDependency(
    int modId,
    int relationType);
