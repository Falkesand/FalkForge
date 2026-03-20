namespace FalkForge.Engine.Tests.Detection;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class PackageDetectorTests
{
    private const string ProductCode = "{12345678-1234-1234-1234-123456789ABC}";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductCode;

    [Fact]
    public void Detect_NoRegistryKeys_ReturnsNotInstalled()
    {
        var registry = new MockRegistry();
        var detector = new PackageDetector(registry);
        var manifest = TestManifestFactory.CreateSimple(
            packages: [TestManifestFactory.CreateMsiPackage(productCode: ProductCode)]);

        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.NotInstalled, result.State);
        Assert.Null(result.CurrentVersion);
    }

    [Fact]
    public void Detect_ProductFoundInHklm_ReturnsInstalled()
    {
        var registry = new MockRegistry()
            .AddKey(RegistryRoot.LocalMachine, UninstallKey)
            .SetStringValue(RegistryRoot.LocalMachine, UninstallKey, "DisplayVersion", "1.0.0");

        var detector = new PackageDetector(registry);
        var manifest = TestManifestFactory.CreateSimple(
            version: "1.0.0",
            packages: [TestManifestFactory.CreateMsiPackage(productCode: ProductCode)]);

        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.Installed, result.State);
        Assert.Equal("1.0.0", result.CurrentVersion);
    }

    [Fact]
    public void Detect_ProductFoundInHkcu_ReturnsInstalled()
    {
        var registry = new MockRegistry()
            .AddKey(RegistryRoot.CurrentUser, UninstallKey)
            .SetStringValue(RegistryRoot.CurrentUser, UninstallKey, "DisplayVersion", "1.0.0");

        var detector = new PackageDetector(registry);
        var manifest = TestManifestFactory.CreateSimple(
            version: "1.0.0",
            packages: [TestManifestFactory.CreateMsiPackage(productCode: ProductCode)]);

        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.Installed, result.State);
        Assert.Equal("1.0.0", result.CurrentVersion);
    }

    [Fact]
    public void Detect_OlderVersionInstalled_ReturnsOlderVersion()
    {
        var registry = new MockRegistry()
            .AddKey(RegistryRoot.LocalMachine, UninstallKey)
            .SetStringValue(RegistryRoot.LocalMachine, UninstallKey, "DisplayVersion", "1.0.0");

        var detector = new PackageDetector(registry);
        var manifest = TestManifestFactory.CreateSimple(
            version: "2.0.0",
            packages: [TestManifestFactory.CreateMsiPackage(productCode: ProductCode)]);

        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.OlderVersion, result.State);
        Assert.Equal("1.0.0", result.CurrentVersion);
    }

    [Fact]
    public void Detect_NewerVersionInstalled_ReturnsNewerVersion()
    {
        var registry = new MockRegistry()
            .AddKey(RegistryRoot.LocalMachine, UninstallKey)
            .SetStringValue(RegistryRoot.LocalMachine, UninstallKey, "DisplayVersion", "3.0.0");

        var detector = new PackageDetector(registry);
        var manifest = TestManifestFactory.CreateSimple(
            version: "2.0.0",
            packages: [TestManifestFactory.CreateMsiPackage(productCode: ProductCode)]);

        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.NewerVersion, result.State);
        Assert.Equal("3.0.0", result.CurrentVersion);
    }

    [Fact]
    public void Detect_NoProductCode_ReturnsNotInstalled()
    {
        var registry = new MockRegistry();
        var detector = new PackageDetector(registry);

        // Package without ProductCode in Properties
        var package = new PackageInfo
        {
            Id = "TestMsi",
            Type = PackageType.MsiPackage,
            DisplayName = "Test",
            SourcePath = @"C:\test\test.msi",
            Sha256Hash = "AABB"
        };

        var manifest = TestManifestFactory.CreateSimple(packages: [package]);

        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.NotInstalled, result.State);
    }

    [Fact]
    public void Detect_ExePackageOnly_ReturnsNotInstalled()
    {
        var registry = new MockRegistry();
        var detector = new PackageDetector(registry);
        var manifest = TestManifestFactory.CreateSimple(
            packages: [TestManifestFactory.CreateExePackage()]);

        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.NotInstalled, result.State);
    }

    [Fact]
    public void Detect_MultiplePackages_DetectsFirstInstalledPackage()
    {
        var productCode1 = "{11111111-1111-1111-1111-111111111111}";
        var productCode2 = "{22222222-2222-2222-2222-222222222222}";
        var uninstallKey2 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + productCode2;

        var registry = new MockRegistry()
            .AddKey(RegistryRoot.LocalMachine, uninstallKey2)
            .SetStringValue(RegistryRoot.LocalMachine, uninstallKey2, "DisplayVersion", "1.0.0");

        var detector = new PackageDetector(registry);
        var manifest = TestManifestFactory.CreateSimple(
            version: "1.0.0",
            packages:
            [
                TestManifestFactory.CreateMsiPackage(id: "Pkg1", productCode: productCode1),
                TestManifestFactory.CreateMsiPackage(id: "Pkg2", productCode: productCode2)
            ]);

        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.Installed, result.State);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", InstallState.Installed)]
    [InlineData("1.0.0", "2.0.0", InstallState.OlderVersion)]
    [InlineData("2.0.0", "1.0.0", InstallState.NewerVersion)]
    [InlineData("1.0.0.0", "1.0.0.0", InstallState.Installed)]
    [InlineData("1.2.3", "1.2.4", InstallState.OlderVersion)]
    public void CompareVersions_ReturnsExpected(string installed, string target, InstallState expected)
    {
        Assert.Equal(expected, PackageDetector.CompareVersions(installed, target));
    }

    [Fact]
    public void CompareVersions_InvalidVersionStrings_ReturnsInstalled()
    {
        // When versions can't be parsed, assume installed
        Assert.Equal(InstallState.Installed, PackageDetector.CompareVersions("not-a-version", "1.0.0"));
    }
}
