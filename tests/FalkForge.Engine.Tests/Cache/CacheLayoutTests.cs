namespace FalkForge.Engine.Tests.Cache;

using FalkForge.Engine.Cache;
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

        Assert.Contains("FalkForge", layout.BasePath);
        Assert.Contains("Cache", layout.BasePath);
    }

    [Fact]
    public void Constructor_PerUserScope_UsesLocalAppData()
    {
        var layout = new CacheLayout(InstallScope.PerUser);

        Assert.Contains("FalkForge", layout.BasePath);
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

        var expected = Path.GetFullPath(
            Path.Combine(@"C:\cache", "12345678-1234-1234-1234-123456789abc", "MyPackage", "setup.msi"));
        Assert.Equal(expected, path);
    }

    [Fact]
    public void GetBundlePath_DifferentBundleIds_ProduceDifferentPaths()
    {
        var layout = new CacheLayout(@"C:\cache");
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        Assert.NotEqual(layout.GetBundlePath(id1), layout.GetBundlePath(id2));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("foo/../../../etc")]
    [InlineData("..")]
    public void GetPackagePath_RejectsPathTraversal(string packageId)
    {
        var layout = new CacheLayout(@"C:\cache");
        var bundleId = Guid.NewGuid();

        var ex = Assert.Throws<ArgumentException>(() => layout.GetPackagePath(bundleId, packageId));
        Assert.Equal("packageId", ex.ParamName);
        Assert.Contains("invalid characters", ex.Message);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config")]
    [InlineData("sub/../../escape.exe")]
    public void GetPayloadPath_RejectsPathTraversal(string fileName)
    {
        var layout = new CacheLayout(@"C:\cache");
        var bundleId = Guid.NewGuid();

        // fileName with directory traversal gets stripped to just the filename by SanitizeFileName,
        // so the path stays contained. Verify no escape occurs.
        var path = layout.GetPayloadPath(bundleId, "ValidPackage", fileName);
        var resolvedBase = Path.GetFullPath(layout.GetBundlePath(bundleId) + Path.DirectorySeparatorChar);
        Assert.StartsWith(resolvedBase, path);
    }

    [Theory]
    [InlineData("subdir/payload.msi", "payload.msi")]
    [InlineData("deep/nested/path/setup.exe", "setup.exe")]
    [InlineData("dir\\file.cab", "file.cab")]
    public void GetPayloadPath_StripsDirectoryComponents(string fileName, string expectedName)
    {
        var layout = new CacheLayout(@"C:\cache");
        var bundleId = new Guid("12345678-1234-1234-1234-123456789abc");

        var path = layout.GetPayloadPath(bundleId, "MyPackage", fileName);

        Assert.EndsWith(expectedName, Path.GetFileName(path));
        var resolvedBase = Path.GetFullPath(layout.GetBundlePath(bundleId) + Path.DirectorySeparatorChar);
        Assert.StartsWith(resolvedBase, path);
    }

    [Theory]
    [InlineData("MyPackage")]
    [InlineData("my-package")]
    [InlineData("my.package.v2")]
    [InlineData("Package_123")]
    [InlineData("A")]
    public void GetPackagePath_AcceptsValidId(string packageId)
    {
        var layout = new CacheLayout(@"C:\cache");
        var bundleId = new Guid("12345678-1234-1234-1234-123456789abc");

        var path = layout.GetPackagePath(bundleId, packageId);

        var expected = Path.Combine(@"C:\cache", "12345678-1234-1234-1234-123456789abc", packageId);
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("  ")]
    public void GetPackagePath_RejectsEmptyOrWhitespace(string packageId)
    {
        var layout = new CacheLayout(@"C:\cache");
        var bundleId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => layout.GetPackagePath(bundleId, packageId));
    }

    [Theory]
    [InlineData("pkg id")]
    [InlineData("pkg@name")]
    [InlineData("pkg;drop")]
    [InlineData("pkg<script>")]
    public void GetPackagePath_RejectsSpecialCharacters(string packageId)
    {
        var layout = new CacheLayout(@"C:\cache");
        var bundleId = Guid.NewGuid();

        var ex = Assert.Throws<ArgumentException>(() => layout.GetPackagePath(bundleId, packageId));
        Assert.Equal("packageId", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void GetPayloadPath_RejectsEmptyFileName(string fileName)
    {
        var layout = new CacheLayout(@"C:\cache");
        var bundleId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => layout.GetPayloadPath(bundleId, "ValidPackage", fileName));
    }
}
