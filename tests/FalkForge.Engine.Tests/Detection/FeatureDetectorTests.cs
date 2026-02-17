namespace FalkForge.Engine.Tests.Detection;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class FeatureDetectorTests
{
    private static readonly Guid TestBundleId = Guid.NewGuid();
    private static readonly string TestBundleIdFormatted = TestBundleId.ToString("B");

    private static ManifestFeature MakeFeature(
        string id,
        string title = "Feature",
        string? description = null,
        bool isDefault = true,
        bool isRequired = false,
        params string[] packageIds) =>
        new(id, title, description, isDefault, isRequired, packageIds);

    [Fact]
    public void Detect_EmptyFeatures_ReturnsEmptyArray()
    {
        var registry = new MockRegistry();
        var packageResults = new Dictionary<string, InstallState>();

        var result = FeatureDetector.Detect([], registry, TestBundleId, InstallScope.PerUser, packageResults);

        Assert.Empty(result);
    }

    [Fact]
    public void Detect_FreshInstall_UsesDefaults()
    {
        var registry = new MockRegistry();
        var packageResults = new Dictionary<string, InstallState>();

        var features = new[]
        {
            MakeFeature("core", "Core", isDefault: true),
            MakeFeature("extras", "Extras", isDefault: false)
        };

        var result = FeatureDetector.Detect(features, registry, TestBundleId, InstallScope.PerUser, packageResults);

        Assert.Equal(2, result.Length);
        Assert.True(result[0].IsSelected);
        Assert.False(result[1].IsSelected);
    }

    [Fact]
    public void Detect_RegistryHasSelections_UsesRegistryValues()
    {
        var registry = new MockRegistry();
        var subKey = $@"SOFTWARE\FalkForge\Burn\{TestBundleIdFormatted}\Features";
        registry.SetStringValue("HKCU", subKey, "core", "1");
        registry.SetStringValue("HKCU", subKey, "extras", "0");

        var packageResults = new Dictionary<string, InstallState>();
        var features = new[]
        {
            MakeFeature("core", "Core", isDefault: false),
            MakeFeature("extras", "Extras", isDefault: true)
        };

        var result = FeatureDetector.Detect(features, registry, TestBundleId, InstallScope.PerUser, packageResults);

        Assert.True(result[0].IsSelected);
        Assert.False(result[1].IsSelected);
    }

    [Fact]
    public void Detect_RegistryMissing_FallsBackToMsiDetection()
    {
        var registry = new MockRegistry();
        var packageResults = new Dictionary<string, InstallState>
        {
            ["pkg1"] = InstallState.Installed,
            ["pkg2"] = InstallState.NotInstalled
        };

        var features = new[]
        {
            MakeFeature("core", "Core", isDefault: false, packageIds: ["pkg1"]),
            MakeFeature("extras", "Extras", isDefault: true, packageIds: ["pkg2"])
        };

        var result = FeatureDetector.Detect(features, registry, TestBundleId, InstallScope.PerUser, packageResults);

        Assert.True(result[0].IsSelected);
        Assert.False(result[1].IsSelected);
    }

    [Fact]
    public void Detect_MsiFallback_AllPackagesInstalled_FeatureSelected()
    {
        var registry = new MockRegistry();
        var packageResults = new Dictionary<string, InstallState>
        {
            ["pkg1"] = InstallState.Installed,
            ["pkg2"] = InstallState.OlderVersion
        };

        var features = new[]
        {
            MakeFeature("multi", "Multi-Package", isDefault: false, packageIds: ["pkg1", "pkg2"])
        };

        var result = FeatureDetector.Detect(features, registry, TestBundleId, InstallScope.PerUser, packageResults);

        Assert.True(result[0].IsSelected);
    }

    [Fact]
    public void Detect_MsiFallback_SomePackagesMissing_FeatureNotSelected()
    {
        var registry = new MockRegistry();
        var packageResults = new Dictionary<string, InstallState>
        {
            ["pkg1"] = InstallState.Installed,
            ["pkg2"] = InstallState.NotInstalled
        };

        var features = new[]
        {
            MakeFeature("partial", "Partial", isDefault: true, packageIds: ["pkg1", "pkg2"])
        };

        var result = FeatureDetector.Detect(features, registry, TestBundleId, InstallScope.PerUser, packageResults);

        Assert.False(result[0].IsSelected);
    }

    [Fact]
    public void Detect_RequiredFeature_AlwaysSelected()
    {
        var registry = new MockRegistry();
        var subKey = $@"SOFTWARE\FalkForge\Burn\{TestBundleIdFormatted}\Features";
        registry.SetStringValue("HKCU", subKey, "required-feat", "0");

        var packageResults = new Dictionary<string, InstallState>();
        var features = new[]
        {
            MakeFeature("required-feat", "Required", isDefault: false, isRequired: true)
        };

        var result = FeatureDetector.Detect(features, registry, TestBundleId, InstallScope.PerUser, packageResults);

        Assert.True(result[0].IsSelected);
        Assert.True(result[0].IsRequired);
    }

    [Fact]
    public void Detect_WasPreviouslyInstalled_TrueWhenDetected()
    {
        var registry = new MockRegistry();
        var subKey = $@"SOFTWARE\FalkForge\Burn\{TestBundleIdFormatted}\Features";
        registry.SetStringValue("HKCU", subKey, "core", "1");

        var packageResults = new Dictionary<string, InstallState>();
        var features = new[]
        {
            MakeFeature("core", "Core", isDefault: false)
        };

        var result = FeatureDetector.Detect(features, registry, TestBundleId, InstallScope.PerUser, packageResults);

        Assert.True(result[0].WasPreviouslyInstalled);
    }

    [Fact]
    public void Detect_WasPreviouslyInstalled_FalseWhenFreshInstall()
    {
        var registry = new MockRegistry();
        var packageResults = new Dictionary<string, InstallState>();

        var features = new[]
        {
            MakeFeature("core", "Core", isDefault: true)
        };

        var result = FeatureDetector.Detect(features, registry, TestBundleId, InstallScope.PerUser, packageResults);

        Assert.False(result[0].WasPreviouslyInstalled);
    }

    [Fact]
    public void Detect_MixedRegistryAndDefaults_HandlesCorrectly()
    {
        // When registry has at least one feature, ALL features use registry as source.
        // Features not found in registry default to their IsDefault value.
        var registry = new MockRegistry();
        var subKey = $@"SOFTWARE\FalkForge\Burn\{TestBundleIdFormatted}\Features";
        registry.SetStringValue("HKLM", subKey, "core", "1");
        // "extras" is NOT in registry

        var packageResults = new Dictionary<string, InstallState>();
        var features = new[]
        {
            MakeFeature("core", "Core", isDefault: false),
            MakeFeature("extras", "Extras", isDefault: true)
        };

        var result = FeatureDetector.Detect(features, registry, TestBundleId, InstallScope.PerMachine, packageResults);

        // core found in registry → selected, previously installed
        Assert.True(result[0].IsSelected);
        Assert.True(result[0].WasPreviouslyInstalled);

        // extras not in registry but registry source was used → falls back to default
        Assert.True(result[1].IsSelected);
        Assert.False(result[1].WasPreviouslyInstalled);
    }
}
