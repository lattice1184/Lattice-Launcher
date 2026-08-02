using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Utils;
using PCL.Core.Minecraft.Saves;

namespace Launcher.App.ViewModels;

/// <summary>
/// 版本管理（PCL2 式）：删除 / 备份 / 导出整合包 / 打开文件夹 / MOD 启停删除 / 存档列表。
/// 目录语义与启动一致：隔离 → versions/{id}；否则共享 .minecraft 根。
/// </summary>
public partial class VersionManageViewModel : ViewModelBase
{
    private readonly string _gameDir;
    private readonly string _versionId;
    private readonly Action _onDeleted;
    private readonly bool _isolated;

    public ObservableCollection<ModItemVM> Mods { get; } = [];
    public ObservableCollection<SaveItemVM> Saves { get; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>删除二次确认（点一次变"确认删除？"）</summary>
    [ObservableProperty]
    public partial bool IsConfirmDelete { get; set; }

    public string ModsCountText => $"MOD（{Mods.Count}）";
    public string SavesCountText => $"存档（{Saves.Count}）";

    /// <summary>版本根目录（隔离 → versions/{id}；否则共享根）</summary>
    private string RootDir => _isolated ? Path.Combine(_gameDir, "versions", _versionId) : _gameDir;

    public VersionManageViewModel(string gameDir, string versionId, Action onDeleted)
    {
        _gameDir = gameDir;
        _versionId = versionId;
        _onDeleted = onDeleted;
        _isolated = LauncherSettings.Current.VersionIsolation;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusText = "加载中…";
        try
        {
            await Task.Run(() =>
            {
                ScanMods();
                ScanSaves();
            });
            OnPropertyChanged(nameof(ModsCountText));
            OnPropertyChanged(nameof(SavesCountText));
            StatusText = "";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------- MOD ----------

    private void ScanMods()
    {
        Mods.Clear();
        if (!Directory.Exists(Path.Combine(RootDir, "mods"))) return;
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RootDir, "mods"), "*.jar*"))
        {
            var file = Path.GetFileName(f);
            var disabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            Mods.Add(new ModItemVM(f, disabled ? file[..^".disabled".Length] : file,
                new FileInfo(f).Length));
        }
        OnPropertyChanged(nameof(ModsCountText));
    }

    [RelayCommand]
    private void ToggleMod(ModItemVM mod)
    {
        try
        {
            var disabled = Path.Combine(Path.GetDirectoryName(mod.Path)!, Path.GetFileName(mod.Path) + ".disabled");
            var enabled = mod.Path;
            File.Move(disabled, enabled); // 启
        }
        catch { /* 禁用路径不存在 → 尝试禁用 */ }
        try
        {
            if (File.Exists(mod.Path))
            {
                File.Move(mod.Path, mod.Path + ".disabled"); // 禁
            }
        }
        catch { }
        ScanMods();
    }

    [RelayCommand]
    private void DeleteMod(ModItemVM mod)
    {
        try { File.Delete(mod.Path); } catch { }
        ScanMods();
    }

    [RelayCommand]
    private void OpenModsFolder() => OpenFolder(Path.Combine(RootDir, "mods"));

    // ---------- 存档（借用 PCL.Core SaveManager 解析 level.dat） ----------

    private void ScanSaves()
    {
        Saves.Clear();
        var savesDir = Path.Combine(RootDir, "saves");
        if (!Directory.Exists(savesDir)) return;
        try
        {
            foreach (var info in new SaveManager().ScanSaveFoldersAsync(savesDir, CancellationToken.None)
                         .GetAwaiter().GetResult())
            {
                Saves.Add(new SaveItemVM(info.LevelName, info.LastPlayedUtc, info.FolderPath));
            }
        }
        catch { /* level.dat 解析失败不阻塞（有文件夹就显示） */ }
        if (Saves.Count == 0)
        {
            foreach (var dir in Directory.EnumerateDirectories(savesDir))
                Saves.Add(new SaveItemVM(Path.GetFileName(dir), DateTime.MinValue, dir));
        }
        OnPropertyChanged(nameof(SavesCountText));
    }

    [RelayCommand]
    private void OpenSaveFolder(SaveItemVM save) => OpenFolder(save.FolderPath);

    [RelayCommand]
    private void OpenSavesFolder() => OpenFolder(Path.Combine(RootDir, "saves"));

    // ---------- 删除 / 备份 / 导出 / 打开 ----------

    [RelayCommand]
    private void Delete()
    {
        if (!IsConfirmDelete)
        {
            IsConfirmDelete = true;
            StatusText = "再次点击确认删除（仅删除版本目录，可先备份）";
            return;
        }
        try
        {
            var dir = Path.Combine(_gameDir, "versions", _versionId);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            _onDeleted();
        }
        catch (Exception ex)
        {
            StatusText = $"删除失败: {ex.Message}";
        }
        finally
        {
            IsConfirmDelete = false;
        }
    }

    [RelayCommand]
    private async Task Backup()
    {
        IsBusy = true;
        StatusText = "备份中…";
        try
        {
            var backupsDir = Path.Combine(_gameDir, "backups");
            Directory.CreateDirectory(backupsDir);
            var zipPath = Path.Combine(backupsDir, $"{_versionId}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            await Task.Run(() => ZipFile.CreateFromDirectory(RootDir, zipPath,
                CompressionLevel.Optimal, includeBaseDirectory: false));
            StatusText = $"已备份 → {zipPath}";
        }
        catch (Exception ex)
        {
            StatusText = $"备份失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportModpack()
    {
        IsBusy = true;
        StatusText = "导出中…";
        try
        {
            var staging = Path.Combine(Path.GetTempPath(), $"yanla-export-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            try
            {
                // 收集整合包内容：mods / saves / config / options.txt
                await Task.Run(() =>
                {
                    foreach (var sub in new[] { "mods", "saves", "config", "resourcepacks", "shaderpacks" })
                    {
                        var src = Path.Combine(RootDir, sub);
                        if (Directory.Exists(src)) CopyDir(src, Path.Combine(staging, sub));
                    }
                    var options = Path.Combine(RootDir, "options.txt");
                    if (File.Exists(options)) File.Copy(options, Path.Combine(staging, "options.txt"));

                    // manifest.json
                    var manifest = new
                    {
                        name = _versionId,
                        version = "1.0",
                        mcVersion = ExtractMcVersion(_versionId),
                        loader = ExtractLoader(_versionId),
                        fileCount = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Count(),
                        sizeBytes = DirSize(staging),
                    };
                    File.WriteAllText(Path.Combine(staging, "manifest.json"),
                        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                });

                var outDir = Path.Combine(_gameDir, "downloads", "modpacks");
                Directory.CreateDirectory(outDir);
                var zipPath = Path.Combine(outDir, $"{_versionId}-整合包.zip");
                await Task.Run(() => ZipFile.CreateFromDirectory(staging, zipPath,
                    CompressionLevel.Optimal, includeBaseDirectory: false));
                StatusText = $"已导出 → {zipPath}";
            }
            finally
            {
                try { Directory.Delete(staging, true); } catch { }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            var dir = Path.Combine(_gameDir, "versions", _versionId);
            if (!Directory.Exists(dir)) dir = _gameDir;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"打开失败: {ex.Message}";
        }
    }

    // ---------- 工具 ----------

    private static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.EnumerateFiles(src))
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.EnumerateDirectories(src))
            CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    private static long DirSize(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f =>
            new FileInfo(f).Length);

    /// <summary>从版本 id 提取 MC 版本（1.21.1-fabric-0.16.9 → 1.21.1；26.2 → 26.2）</summary>
    private static string ExtractMcVersion(string id)
    {
        var m = System.Text.RegularExpressions.Regex.Match(id, @"^\d+\.\d+(\.\d+)?");
        return m.Success ? m.Value : id;
    }

    private static string ExtractLoader(string id)
    {
        var lower = id.ToLowerInvariant();
        foreach (var (kw, name) in new[] { ("fabric", "fabric"), ("neoforge", "neoforge"),
                     ("forge", "forge"), ("quilt", "quilt") })
        {
            if (lower.Contains(kw)) return name;
        }
        return "vanilla";
    }
}

/// <summary>MOD 文件项（名称 / 路径 / 禁用态 / 大小）</summary>
public sealed record ModItemVM(string Path, string Name, long SizeBytes)
{
    public string SizeText => SizeBytes >= 1024 * 1024 ? $"{SizeBytes / 1024.0 / 1024:0.0} MB" : $"{SizeBytes / 1024:0} KB";
}

/// <summary>存档项（世界名 / 最后游玩 / 路径，来自 PCL.Core SaveManager 解析 level.dat）</summary>
public sealed record SaveItemVM(string LevelName, DateTime LastPlayedUtc, string FolderPath)
{
    public string LastPlayedText => LastPlayedUtc.Year > 2000 ? $"最后游玩 {LastPlayedUtc.ToLocalTime():yyyy-MM-dd HH:mm}" : "";
}
