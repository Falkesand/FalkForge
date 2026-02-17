namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Engine.Variables;
using Xunit;

public sealed class FeaturePersistenceTests
{
    private static readonly Guid TestBundleId = new("12345678-1234-1234-1234-123456789012");

    private static ManifestFeature MakeFeature(string id) =>
        new(id, id, null, true, false, []);

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var registry = new MockRegistry();
        var selections = new Dictionary<string, bool>
        {
            ["core"] = true,
            ["extras"] = false,
            ["docs"] = true
        };
        var features = new ManifestFeature[]
        {
            MakeFeature("core"),
            MakeFeature("extras"),
            MakeFeature("docs")
        };

        FeaturePersistence.SaveFeatureSelections(
            registry, TestBundleId, InstallScope.PerUser, selections);

        var loaded = FeaturePersistence.LoadFeatureSelections(
            registry, TestBundleId, InstallScope.PerUser, features);

        Assert.Equal(3, loaded.Count);
        Assert.True(loaded["core"]);
        Assert.False(loaded["extras"]);
        Assert.True(loaded["docs"]);
    }

    [Fact]
    public void Load_EmptyRegistry_ReturnsEmptyDictionary()
    {
        var registry = new MockRegistry();
        var features = new ManifestFeature[] { MakeFeature("core") };

        var loaded = FeaturePersistence.LoadFeatureSelections(
            registry, TestBundleId, InstallScope.PerUser, features);

        Assert.Empty(loaded);
    }

    [Fact]
    public void Save_WritesCorrectValues()
    {
        var registry = new MockRegistry();
        var selections = new Dictionary<string, bool>
        {
            ["selected"] = true,
            ["unselected"] = false
        };

        FeaturePersistence.SaveFeatureSelections(
            registry, TestBundleId, InstallScope.PerUser, selections);

        var keyPath = $@"SOFTWARE\FalkForge\Burn\{TestBundleId:B}\Features";
        Assert.Equal("1", registry.GetStringValue("HKCU", keyPath, "selected"));
        Assert.Equal("0", registry.GetStringValue("HKCU", keyPath, "unselected"));
    }

    [Fact]
    public void Clear_DeletesKey()
    {
        var registry = new MockRegistry();
        var keyPath = $@"SOFTWARE\FalkForge\Burn\{TestBundleId:B}\Features";
        registry.SetStringValue("HKLM", keyPath, "core", "1");

        Assert.True(registry.KeyExists("HKLM", keyPath));

        FeaturePersistence.ClearFeatureSelections(
            registry, TestBundleId, InstallScope.PerMachine);

        Assert.False(registry.KeyExists("HKLM", keyPath));
    }

    [Fact]
    public void Save_PerMachine_UsesHKLM()
    {
        var registry = new MockRegistry();
        var selections = new Dictionary<string, bool> { ["core"] = true };

        FeaturePersistence.SaveFeatureSelections(
            registry, TestBundleId, InstallScope.PerMachine, selections);

        var keyPath = $@"SOFTWARE\FalkForge\Burn\{TestBundleId:B}\Features";
        Assert.Equal("1", registry.GetStringValue("HKLM", keyPath, "core"));
        Assert.Null(registry.GetStringValue("HKCU", keyPath, "core"));
    }

    [Fact]
    public void Save_PerUser_UsesHKCU()
    {
        var registry = new MockRegistry();
        var selections = new Dictionary<string, bool> { ["core"] = true };

        FeaturePersistence.SaveFeatureSelections(
            registry, TestBundleId, InstallScope.PerUser, selections);

        var keyPath = $@"SOFTWARE\FalkForge\Burn\{TestBundleId:B}\Features";
        Assert.Equal("1", registry.GetStringValue("HKCU", keyPath, "core"));
        Assert.Null(registry.GetStringValue("HKLM", keyPath, "core"));
    }
}
