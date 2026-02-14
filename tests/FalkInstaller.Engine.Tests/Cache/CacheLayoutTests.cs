namespace FalkInstaller.Engine.Tests.Cache;

using FalkInstaller.Engine.Cache;
using Xunit;

public sealed class CacheLayoutTests
{
    [Fact]
    public void Constructor_WithBasePath_SetsBasePath()
    {
        var layout = new CacheLayout(@"C:\test\cache");

        Assert.Equal(@"C:\test\cache", layout.BasePath);
    }

    [Fact]
    public void Constructor_PerMachineScope_UsesCommonAppData()
    {
        var layout = new CacheLayout(InstallScope.PerMachine);

        Assert.Contains("FalkInstaller", layout.BasePath);
        Assert.Contains("Cache", layout.BasePath);
    }

    [Fact]
    public void Constructor_PerUserScope_UsesLocalAppData()
    {
        var layout = new CacheLayout(InstallScope.PerUser);

        Assert.Contains("FalkInstaller", layout.BasePath);
        Assert.Contains("Cache", layout.BasePath);
    }

    [Fact]
    public void GetBundlePath_FormatsBundleIdCorrectly()
    {
        var layout = new CacheLayout(@"C:\cache");
        var bundleId = new Guid("12345678-1234-1234-1234-123456789abc");

        var path = layout.GetBundlePath(bundleId);

        Assert.Equal(
            Path.Combine(@"C:\cache", "12345678-1234-1234-1234-123456789abc"),
            path);
    }

    [Fact]
    public void GetPackagePath_IncludesBundleAndPackageId()
    {
        var layout = new CacheLayout(@"C:\cache");
        var bundleId = new Guid("12345678-1234-1234-1234-123456789abc");

        var path = layout.GetPackagePath(bundleId, "MyPackage");

        Assert.Equal(
            Path.Combine(@"C:\cache", "12345678-1234-1234-1234-123456789abc", "MyPackage"),
            path);
    }

    [Fact]
    public void GetPayloadPath_IncludesFullHierarchy()
    {
        var layout = new CacheLayout(@"C:\cache");
        var bundleId = new Guid("12345678-1234-1234-1234-123456789abc");

        var path = layout.GetPayloadPath(bundleId, "MyPackage", "setup.msi");

        Assert.Equal(
            Path.Combine(@"C:\cache", "12345678-1234-1234-1234-123456789abc", "MyPackage", "setup.msi"),
            path);
    }

    [Fact]
    public void GetBundlePath_DifferentBundleIds_ProduceDifferentPaths()
    {
        var layout = new CacheLayout(@"C:\cache");
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        Assert.NotEqual(layout.GetBundlePath(id1), layout.GetBundlePath(id2));
    }
}
