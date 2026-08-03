using Launcher.Core.Account;

namespace Launcher.Core.Tests;

/// <summary>账号服务：离线登录 / 多账号持久化 / 切换 / 删除</summary>
public class AccountServiceTests
{
    private static string TempStore() => Path.Combine(Path.GetTempPath(), $"accounts-{Guid.NewGuid():N}.json");

    [Fact]
    public void LoginOffline_MultipleAccounts_Persisted()
    {
        var path = TempStore();
        try
        {
            var svc = new AccountService(path);
            var a = svc.LoginOffline("Steve");
            var b = svc.LoginOffline("Alex");
            Assert.Equal(2, svc.Accounts.Count);
            Assert.Equal(b.Name, svc.Current!.Name);

            // 重载：列表与当前账号保持
            var reloaded = new AccountService(path);
            reloaded.Load();
            Assert.Equal(2, reloaded.Accounts.Count);
            Assert.Equal("Alex", reloaded.Current!.Name);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LoginOffline_SameName_Deduplicates()
    {
        var path = TempStore();
        try
        {
            var svc = new AccountService(path);
            svc.LoginOffline("Steve");
            svc.LoginOffline("steve"); // 大小写不敏感 → 覆盖
            Assert.Single(svc.Accounts);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SwitchTo_ChangesCurrent()
    {
        var path = TempStore();
        try
        {
            var svc = new AccountService(path);
            svc.LoginOffline("Steve");
            svc.LoginOffline("Alex");
            Assert.True(svc.SwitchTo("Steve"));
            Assert.Equal("Steve", svc.Current!.Name);
            Assert.False(svc.SwitchTo("Nobody")); // 不存在 → false
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Delete_CurrentAccount_LogsOut()
    {
        var path = TempStore();
        try
        {
            var svc = new AccountService(path);
            svc.LoginOffline("Steve");
            svc.LoginOffline("Alex");
            Assert.True(svc.Delete("Alex")); // 当前账号
            Assert.Null(svc.Current);
            Assert.Single(svc.Accounts);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void OfflineUuid_Stable_V3Format()
    {
        var uuid = AccountService.OfflineUuid("Steve");
        Assert.Equal(36, uuid.Length);
        Assert.Equal(uuid, AccountService.OfflineUuid("Steve")); // 稳定
        Assert.NotEqual(uuid, AccountService.OfflineUuid("Alex"));
        Assert.Equal('3', uuid[14]); // UUID v3
    }

    [Fact]
    public void Changed_Event_FiresOnLoginAndLogout()
    {
        var path = TempStore();
        try
        {
            var svc = new AccountService(path);
            var count = 0;
            svc.Changed += () => count++;
            svc.LoginOffline("Steve");
            Assert.Equal(1, count);
            svc.SwitchTo("Steve");
            Assert.Equal(2, count);
            svc.Logout();
            Assert.Equal(3, count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
