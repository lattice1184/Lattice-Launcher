namespace Launcher.Core.Launch;

/// <summary>
/// 自动中文：启动前把 <game>/options.txt 的语言键合并为 zh_cn，保留其他设置。
/// </summary>
public static class AutoChinese
{
    public static void Apply(string gameDirectory)
    {
        try
        {
            var optionsPath = Path.Combine(gameDirectory, "options.txt");
            var lines = File.Exists(optionsPath)
                ? File.ReadAllLines(optionsPath).ToList()
                : [];

            var langIndex = lines.FindIndex(l => l.StartsWith("lang:"));
            var langLine = "lang:zh_cn";
            if (langIndex >= 0) lines[langIndex] = langLine;
            else lines.Add(langLine);

            File.WriteAllLines(optionsPath, lines);
        }
        catch { /* options.txt 写入失败不阻塞启动 */ }
    }
}
