namespace FalkForge.Engine.Tests.Detection;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class SearchConditionDetectionTests
{
    private const string ProductCode = "{12345678-1234-1234-1234-123456789ABC}";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductCode;

    private static PackageInfo CreateMsiWithSearchConditions(
        DetectionMode mode,
        params SearchCondition[] conditions)
    {
        return new PackageInfo
        {
            Id = "TestMsi",
            Type = PackageType.MsiPackage,
            DisplayName = "Test MSI",
            SourcePath = @"C:\test\test.msi",
            Sha256Hash = "AABB",
            Properties = new Dictionary<string, string> { ["ProductCode"] = ProductCode },
            DetectionMode = mode,
            SearchConditions = conditions
        };
    }

    [Fact]
    public void Detect_DefaultMode_IgnoresSearchConditions()
    {
        // Product is installed per registry, search condition says file is missing.
        // Default mode should ignore search conditions entirely.
        var registry = new MockRegistry()
            .AddKey(RegistryRoot.LocalMachine, UninstallKey)
            .SetStringValue(RegistryRoot.LocalMachine, UninstallKey, "DisplayVersion", "1.0.0");
        var fs = new MockFileSystemProvider(); // No files exist

        var package = CreateMsiWithSearchConditions(
            DetectionMode.Default,
            new SearchCondition
            {
                Type = SearchConditionType.FileExists,
                Path = @"C:\Program Files\App\app.exe"
            });

        var manifest = TestManifestFactory.CreateSimple(
            version: "1.0.0",
            packages: [package]);

        var detector = new PackageDetector(registry, fs);
        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.Installed, result.State);
    }

    [Fact]
    public void Detect_SearchOnlyMode_UsesOnlySearchConditions()
    {
        // Product is NOT installed per registry, but search condition says file exists.
        // SearchOnly mode should detect as installed based on search conditions alone.
        var registry = new MockRegistry();
        var fs = new MockFileSystemProvider()
            .WithFile(@"C:\Program Files\App\app.exe");

        var package = CreateMsiWithSearchConditions(
            DetectionMode.SearchOnly,
            new SearchCondition
            {
                Type = SearchConditionType.FileExists,
                Path = @"C:\Program Files\App\app.exe"
            });

        var manifest = TestManifestFactory.CreateSimple(packages: [package]);

        var detector = new PackageDetector(registry, fs);
        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.Installed, result.State);
    }

    [Fact]
    public void Detect_CombinedMode_BothMustAgree()
    {
        // Product IS installed per registry, but search condition file is missing.
        // Combined mode requires both to agree -- should be NotInstalled.
        var registry = new MockRegistry()
            .AddKey(RegistryRoot.LocalMachine, UninstallKey)
            .SetStringValue(RegistryRoot.LocalMachine, UninstallKey, "DisplayVersion", "1.0.0");
        var fs = new MockFileSystemProvider(); // No files

        var package = CreateMsiWithSearchConditions(
            DetectionMode.Combined,
            new SearchCondition
            {
                Type = SearchConditionType.FileExists,
                Path = @"C:\Program Files\App\app.exe"
            });

        var manifest = TestManifestFactory.CreateSimple(
            version: "1.0.0",
            packages: [package]);

        var detector = new PackageDetector(registry, fs);
        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.NotInstalled, result.State);
    }

    [Fact]
    public void Detect_SearchConditionFails_ReturnsNotInstalled()
    {
        // SearchOnly mode with failing search condition (file doesn't exist).
        var registry = new MockRegistry();
        var fs = new MockFileSystemProvider();

        var package = CreateMsiWithSearchConditions(
            DetectionMode.SearchOnly,
            new SearchCondition
            {
                Type = SearchConditionType.FileExists,
                Path = @"C:\Program Files\App\missing.exe"
            });

        var manifest = TestManifestFactory.CreateSimple(packages: [package]);

        var detector = new PackageDetector(registry, fs);
        var result = detector.Detect(manifest);

        Assert.Equal(InstallState.NotInstalled, result.State);
    }
}
