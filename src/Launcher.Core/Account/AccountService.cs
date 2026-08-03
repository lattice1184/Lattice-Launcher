using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Launcher.Core.Account;

/// <summary>
/// 账号服务：离线账号（UUID v3）+ 多账号持久化 + 当前账号切换。
/// 微软正版登录为后续扩展（F4b，需 ClientId）。
/// </summary>
public sealed class AccountService
{
    /// <summary>进程级共享实例（账号页/主页/启动链路统一读同一状态，跨页面实时同步）</summary>
    public static AccountService Shared { get; } = new();

    /// <summary>账号状态变化（登录/切换/删除/退出）——主页玩家区等订阅实时刷新</summary>
    public event Action? Changed;

    private readonly string _storePath;

    public AccountInfo? Current { get; private set; }

    /// <summary>全部已保存账号（离线多账号；正版接入后并入）</summary>
    public IReadOnlyList<AccountInfo> Accounts => _accounts;

    private List<AccountInfo> _accounts = [];

    public AccountService(string? storePath = null)
    {
        _storePath = storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Launcher", "accounts.json");
    }

    public AccountInfo LoginOffline(string name)
    {
        var uuid = OfflineUuid(name);
        var acc = new AccountInfo(name, uuid, "offline");
        // 重名覆盖（同账号不重复存）；新名追加
        var existing = _accounts.FindIndex(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _accounts[existing] = acc;
        else _accounts.Add(acc);
        Current = acc;
        Save();
        Changed?.Invoke();
        return acc;
    }

    /// <summary>切换当前账号（按名称；不存在则忽略）</summary>
    public bool SwitchTo(string name)
    {
        var acc = _accounts.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (acc is null) return false;
        Current = acc;
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>删除账号（当前账号被删则退出登录）</summary>
    public bool Delete(string name)
    {
        var removed = _accounts.RemoveAll(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed && Current?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
            Current = null;
        if (removed)
        {
            Save();
            Changed?.Invoke();
        }
        return removed;
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return;
            var json = File.ReadAllText(_storePath);
            var saved = JsonSerializer.Deserialize<StoredState>(json);
            if (saved is null) return;
            _accounts = (saved.Accounts ?? [])
                .Select(a => new AccountInfo(a.Name, a.Uuid, a.Type))
                .ToList();
            Current = saved.CurrentName is { } cur
                ? _accounts.FirstOrDefault(a => a.Name == cur)
                : null;
        }
        catch (Exception) { /* 存储损坏则忽略 */ }
    }

    public void Logout()
    {
        Current = null;
        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            var stored = new StoredState(
                Current?.Name,
                _accounts.Select(a => new StoredAccount(a.Name, a.Uuid, a.Type)).ToList());
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

    private sealed record StoredState(string? CurrentName, List<StoredAccount> Accounts);
    private sealed record StoredAccount(string Name, string Uuid, string Type);
}
