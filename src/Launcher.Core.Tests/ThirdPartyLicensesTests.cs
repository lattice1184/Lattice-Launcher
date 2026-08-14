using Launcher.App.Models;

namespace Launcher.Core.Tests;

/// <summary>关于页开源声明清单完整性：每项关键字段非空，声明不重复</summary>
public class ThirdPartyLicensesTests
{
    [Fact]
    public void ProjectNotices_AllNonEmpty()
    {
        Assert.NotEmpty(ThirdPartyLicenses.ProjectNotices);
        Assert.All(ThirdPartyLicenses.ProjectNotices, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(ThirdPartyLicenses.ProjectNotices.Length,
            ThirdPartyLicenses.ProjectNotices.Distinct().Count());
    }

    [Fact]
    public void Packages_AllHaveNameVersionLicense()
    {
        Assert.NotEmpty(ThirdPartyLicenses.Packages);
        Assert.All(ThirdPartyLicenses.Packages, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.False(string.IsNullOrWhiteSpace(p.Version));
            Assert.False(string.IsNullOrWhiteSpace(p.License));
        });
    }

    [Fact]
    public void Packages_NoDuplicateNames()
    {
        Assert.Equal(ThirdPartyLicenses.Packages.Length,
            ThirdPartyLicenses.Packages.Select(p => p.Name).Distinct().Count());
    }
}
