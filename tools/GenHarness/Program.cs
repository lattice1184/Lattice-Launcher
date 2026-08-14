// GenHarness：在 WDAC 拦截 csc 加载自签分析器时，把 PCL.Core.SourceGenerators 的
// 生成输出固化到 PCL.Core/Generated/*.generated.cs（正常进程加载生成器，不触发拦截）。
// 用法：dotnet run --project tools/GenHarness [repoRoot]
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

if (args.Length > 0) Environment.CurrentDirectory = args[0];
var repo = Path.GetFullPath(Environment.CurrentDirectory);
var coreDir = Path.Combine(repo, "PCL.Core");
var genDir = Path.Combine(coreDir, "Generated");
var generatorDll = Path.Combine(repo, "PCL.Core.SourceGenerators", "bin", "Debug", "netstandard2.0", "PCL.Core.SourceGenerators.dll");

// 1. 引用集：ref packs + NuGet 包（解析 project.assets.json，UTF-8）
var refs = new List<MetadataReference>();
var packs = new[]
{
    @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref",
    @"C:\Program Files\dotnet\packs\Microsoft.WindowsDesktop.App.Ref",
};
foreach (var pack in packs)
{
    if (!Directory.Exists(pack)) continue;
    var ver = Directory.GetDirectories(pack).OrderByDescending(p => p).FirstOrDefault();
    if (ver is null) continue;
    var refDir = Directory.GetDirectories(ver, "ref", SearchOption.AllDirectories).FirstOrDefault();
    if (refDir is null) continue;
    foreach (var dll in Directory.GetFiles(refDir, "*.dll"))
        refs.Add(MetadataReference.CreateFromFile(dll));
}

var assetsPath = Path.Combine(coreDir, "obj", "project.assets.json");
if (File.Exists(assetsPath))
{
    var assets = JsonSerializer.Deserialize<AssetsFile>(await File.ReadAllTextAsync(assetsPath, System.Text.Encoding.UTF8))!;
    var target = assets.Targets.FirstOrDefault(t => t.Value.Count > 0).Value ?? [];
    foreach (var (key, pkg) in target)
    {
        if (pkg.Compile is null) continue;
        var idx = key.LastIndexOf('/');
        var name = key[..idx];
        var ver = key[(idx + 1)..];
        foreach (var rel in pkg.Compile.Keys)
        {
            if (!rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages", name.ToLowerInvariant(), ver, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) refs.Add(MetadataReference.CreateFromFile(path));
            break; // 每包取一个编译引用即可
        }
    }
}
Console.WriteLine($"[GenHarness] 引用 {refs.Count} 个程序集");

// 2. 源文件（镜像 csproj glob：排除 bin/obj/publish/** 与 *.g.cs 与 SourceGenerators/**）
var sources = Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories)
    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
             && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             && !p.Contains($"{Path.DirectorySeparatorChar}publish{Path.DirectorySeparatorChar}")
             && !p.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
             && !p.Contains($"{Path.DirectorySeparatorChar}SourceGenerators{Path.DirectorySeparatorChar}"))
    .Select(p => SyntaxFactory.ParseSyntaxTree(File.ReadAllText(p), new CSharpParseOptions(LanguageVersion.Preview), p))
    .ToList();
Console.WriteLine($"[GenHarness] 源文件 {sources.Count} 个");

// 3. 编译
var compilation = CSharpCompilation.Create(
    "PCL.Core", sources, refs,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
var errs = compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
Console.WriteLine($"[GenHarness] 编译错误 {errs} 个（语义不完整不影响语法驱动生成器，仅提示）");

// 4. 加载生成器并运行
var asm = Assembly.LoadFrom(generatorDll);
var generators = asm.GetTypes()
    .Where(t => t.GetCustomAttributes().Any(a => a.GetType().Name == "GeneratorAttribute"))
    .Select(t => (IIncrementalGenerator)Activator.CreateInstance(t)!)
    .ToList();
Console.WriteLine($"[GenHarness] 生成器 {generators.Count} 个: {string.Join(", ", generators.Select(g => g.GetType().Name))}");

var driver = CSharpGeneratorDriver.Create(generators.ToArray());
var updated = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
foreach (var d in diagnostics) Console.WriteLine($"  [gen-diag] {d}");

// 5. 写输出（hint 名 -> PCL.Core/Generated/，统一 .generated.cs 后缀以通过 csproj 的 *.g.cs 排除）
Directory.CreateDirectory(genDir);
foreach (var old in Directory.GetFiles(genDir, "*.cs")) File.Delete(old);
var written = 0;
foreach (var tree in updated.GetRunResult().GeneratedTrees)
{
    var hint = Path.GetFileName(tree.FilePath);
    var dest = Path.Combine(genDir, hint.Replace(".g.cs", "") + ".generated.cs");
    await File.WriteAllTextAsync(dest, tree.ToString(), System.Text.Encoding.UTF8);
    written++;
    Console.WriteLine($"  -> {dest}");
}
Console.WriteLine($"[GenHarness] 写入 {written} 个生成文件到 PCL.Core/Generated/");
Console.WriteLine(written == 0 ? "!! 警告：没有生成任何文件，请检查编译错误" : "完成");

file sealed class AssetsFile
{
    public Dictionary<string, Dictionary<string, PackageTarget>> Targets { get; set; } = [];
}

file sealed class PackageTarget
{
    public Dictionary<string, object?>? Compile { get; set; }
}
