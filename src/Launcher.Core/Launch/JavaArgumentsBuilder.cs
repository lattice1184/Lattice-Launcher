using System.Text.Json;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Launch;

/// <summary>
/// JVM 与游戏参数组装：从版本 JSON 展开 libraries（rules + natives）→ classpath，
/// 按 新版 arguments / 旧版 minecraftArguments 组装完整启动参数。
/// </summary>
public sealed class JavaArgumentsBuilder
{
    private readonly RulesResolver _rules;

    public JavaArgumentsBuilder(RulesResolver? rules = null) => _rules = rules ?? new RulesResolver();

    /// <summary>启动档案：完整参数列表 + 供日志展示的参数快照</summary>
    public sealed record LaunchProfile(
        string JavaPath,
        string[] JvmArgs,
        string[] GameArgs,
        string WorkingDirectory,
        string ClassPath,
        string MainClass,
        string Log4jConfigPath,
        string NativesDirectory,
        string[] NativeJars);

    /// <summary>
    /// 构建启动档案。
    /// </summary>
    /// <param name="version">版本 JSON（已解析）</param>
    /// <param name="gameDir">游戏目录（.minecraft）</param>
    /// <param name="javaPath">Java 可执行文件完整路径</param>
    /// <param name="accountName">用户名（离线）</param>
    /// <param name="accountUuid">UUID（离线为 OfflinePlayer 哈希）</param>
    /// <param name="accessToken">访问令牌（离线固定值）</param>
    /// <param name="memoryMb">内存上限 MB</param>
    /// <param name="extraJvmArgs">额外 JVM 参数（性能管线等）</param>
    /// <param name="language">游戏语言（默认 zh_cn 自动中文）</param>
    public LaunchProfile Build(
        VersionJson version, string gameDir, string javaPath,
        string accountName, string accountUuid, string accessToken,
        long memoryMb, string[]? extraJvmArgs = null)
    {
        // 防御：版本 id 拼入文件路径前净化（拒绝 .. 与分隔符）
        var safeId = version.Id.Replace("..", "").Replace('/', '_').Replace('\\', '_');
        var versionDir = Path.Combine(gameDir, "versions", safeId);
        var librariesDir = Path.Combine(gameDir, "libraries");
        var assetsDir = Path.Combine(gameDir, "assets");

        // 1. classpath：client jar + 过滤后的 libraries（同时收集 natives jar 供解压）
        var classPathParts = new List<string>
        {
            Path.Combine(versionDir, $"{safeId}.jar"),
        };
        var nativesJars = new List<string>();
        foreach (var lib in version.Libraries ?? [])
        {
            if (!_rules.IsAllowed(lib.Rules)) continue;
            // natives 判定：旧版 natives 字段映射 或 新版独立条目（classifier 以 natives- 开头且含 OS 名）
            var (isNative, nativeFullName, oldStyle) = ResolveNativeClassifier(lib);
            if (isNative && nativeFullName is not null)
            {
                var nativeName = Utils.MavenPath.FileName(nativeFullName);
                var relNative = Utils.MavenPath.DirectoryPath(nativeFullName).Replace('/', Path.DirectorySeparatorChar);
                var nativeJar = Path.Combine(librariesDir, relNative, nativeName);
                if (File.Exists(nativeJar)) nativesJars.Add(nativeJar);
                // 旧版（1.12.2 及以下）natives classifier jar 在 classpath；新版只解压
                if (oldStyle) classPathParts.Add(nativeJar);
                continue;
            }
            if (lib.Downloads?.Artifact is { } artifact)
            {
                var rel = Utils.MavenPath.FullPath(lib.Name).Replace('/', Path.DirectorySeparatorChar);
                classPathParts.Add(Path.Combine(librariesDir, rel));
            }
        }
        var classPath = string.Join(Path.PathSeparator, classPathParts);

        // 2. natives 目录（启动前解压 dll）
        var nativesDir = Path.Combine(versionDir, $"{safeId}-natives");
        Directory.CreateDirectory(nativesDir);

        // 3. 参数模板
        var jvmArgs = new List<string>
        {
            $"-Xmx{memoryMb}m",
            "-XX:+UseG1GC",
            $"-Djava.library.path={nativesDir}",
            "-Dminecraft.launcher.brand=YanKaLauncher",
            "-Dminecraft.launcher.version=0.1.0",
            "-Dlog4j.configurationFile=" + (version.Logging?.Client?.File?.Url is { } logUrl
                ? "file:///" + Path.Combine(assetsDir, "log_configs", Path.GetFileName(new Uri(logUrl).LocalPath)).Replace('\\', '/')
                : ""),
        };
        if (extraJvmArgs is not null) jvmArgs.AddRange(extraJvmArgs);

        // 4. 游戏参数
        var gameArgs = BuildGameArgs(version, gameDir, assetsDir, accountName, accountUuid, accessToken);

        return new LaunchProfile(javaPath, [.. jvmArgs], gameArgs, gameDir, classPath,
            version.MainClass ?? "net.minecraft.client.main.Main", "", nativesDir, [.. nativesJars]);
    }

    private string[] BuildGameArgs(
        VersionJson version, string gameDir, string assetsDir,
        string accountName, string accountUuid, string accessToken)
    {
        var gameDirArg = gameDir.Replace('\\', '/');
        var assetsIndexId = version.AssetIndex?.Id ?? version.Assets ?? "legacy";
        var tokens = new Dictionary<string, string>
        {
            ["auth_player_name"] = accountName,
            ["auth_uuid"] = accountUuid,
            ["auth_access_token"] = accessToken,
            ["auth_session"] = accessToken,
            ["version_name"] = version.Id,
            ["game_directory"] = gameDirArg,
            ["game_assets"] = Path.Combine(assetsDir, "legacy").Replace('\\', '/'),
            ["assets_root"] = assetsDir.Replace('\\', '/'),
            ["assets_index_name"] = assetsIndexId,
            ["user_properties"] = "{}",
            ["user_type"] = "legacy",
            ["version_type"] = version.Type ?? "release",
            ["resolution_width"] = "854",
            ["resolution_height"] = "480",
        };

        // 新版：arguments.game（混合字符串与规则对象）
        if (version.Arguments?.Game is { } gameList)
        {
            var args = new List<string>();
            foreach (var el in gameList)
            {
                if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                    args.Add(ReplaceTokens(el.GetString()!, tokens));
                else if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var rules = el.GetProperty("rules").Deserialize<List<RuleJson>>();
                    if (_rules.IsAllowed(rules))
                    {
                        var value = el.GetProperty("value");
                        if (value.ValueKind == System.Text.Json.JsonValueKind.String)
                            args.Add(ReplaceTokens(value.GetString()!, tokens));
                        else if (value.ValueKind == System.Text.Json.JsonValueKind.Array)
                            foreach (var v in value.EnumerateArray())
                                args.Add(ReplaceTokens(v.GetString()!, tokens));
                    }
                }
            }
            return [.. args];
        }

        // 旧版：minecraftArguments 空格分割 + token 替换
        if (version.MinecraftArguments is { } legacy)
        {
            return legacy.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => ReplaceTokens(a, tokens))
                .ToArray();
        }

        return [];
    }

    /// <summary>解析 natives：(是否为 natives, 完整 Maven 名, 是否旧版样式)。旧版 natives 字段映射需拼 classifier；新版条目名自带。</summary>
    private (bool IsNative, string? NativeFullName, bool OldStyle) ResolveNativeClassifier(LibraryJson lib)
    {
        // 旧版：natives 字段按 OS 映射（classifier 同条目，进 classpath）
        if (lib.Natives is { } natives && natives.TryGetValue(_rules.OsName, out var mappedKey))
            return (true, lib.Name + ":" + mappedKey, true);
        // 新版：独立条目名字带 :natives-xxx classifier（如 org.lwjgl:lwjgl-stb:3.3.1:natives-windows）
        var parts = lib.Name.Split(':');
        if (parts.Length == 4 && parts[3].StartsWith("natives-", StringComparison.OrdinalIgnoreCase)
            && parts[3].Contains(_rules.OsName, StringComparison.OrdinalIgnoreCase))
            return (true, lib.Name, false);
        return (false, null, false);
    }

    private static string ReplaceTokens(string arg, Dictionary<string, string> tokens)
    {
        foreach (var (key, value) in tokens)
            arg = arg.Replace("${" + key + "}", value);
        return arg;
    }
}
