using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Launcher.Core.Account;

/// <summary>
/// 账号服务：离线账号（UUID v3）+ 账号持久化。微软正版登录为后续扩展（F4b，需 ClientId）。
/// </summary>
public sealed class AccountService
{
    private readonly string _storePath;

    public AccountInfo? Current { get; private set; }

    public AccountService(string? storePath = null)
    {
        _storePath = storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Launcher", "accounts.json");
    }

    public AccountInfo LoginOffline(string name)
    {
        var uuid = OfflineUuid(name);
        Current = new AccountInfo(name, uuid, "offline");
        Save();
        return Current;
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return;
            var json = File.ReadAllText(_storePath);
            var saved = JsonSerializer.Deserialize<StoredAccount>(json);
            if (saved is not null && !string.IsNullOrEmpty(saved.Name))
                Current = new AccountInfo(saved.Name, saved.Uuid, saved.Type);
        }
        catch (Exception) { /* 存储损坏则忽略 */ }
    }

    public void Logout() { Current = null; Save(); }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            var stored = Current is null ? null : new StoredAccount(Current.Name, Current.Uuid, Current.Type);
            File.WriteAllText(_storePath, JsonSerializer.Serialize(stored));
        }
        catch (Exception) { /* 存储失败不阻塞登录 */ }
    }

    /// <summary>离线 UUID v3：MD5("OfflinePlayer:" + name) 按 Java UUID.nameUUIDFromBytes 格式</summary>
    public static string OfflineUuid(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30); // version 3
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant
        var hex = Convert.ToHexStringLower(bytes);
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    public sealed record AccountInfo(string Name, string Uuid, string Type);

    private sealed record StoredAccount(string Name, string Uuid, string Type);
}
