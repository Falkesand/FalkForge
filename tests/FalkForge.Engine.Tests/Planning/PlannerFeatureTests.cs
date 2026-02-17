namespace FalkForge.Engine.Tests.Planning;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;
using Xunit;

public sealed class PlannerFeatureTests
{
    private static DetectionResult NotInstalledDetection =>
        new(InstallState.NotInstalled, null, []);

    private static PackageInfo CreatePackage(string id, string? installCondition = null)
    {
        return new PackageInfo
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Package {id}",
            SourcePath = $@"C:\test\{id}.msi",
            Sha256Hash = "AABBCCDD",
            InstallCondition = installCondition
        };
    }

    private static InstallerManifest CreateManifest(
        PackageInfo[] packages,
        ManifestFeature[]? features = null)
    {
        return new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = packages,
            Features = features ?? []
        };
    }

    [Fact]
    public void Plan_NoFeatures_AllPackagesIncluded()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
        [
            CreatePackage("Pkg1"),
            CreatePackage("Pkg2"),
            CreatePackage("Pkg3")
        ]);

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Actions.Count);
        Assert.Equal("Pkg1", result.Value.Actions[0].PackageId);
        Assert.Equal("Pkg2", result.Value.Actions[1].PackageId);
        Assert.Equal("Pkg3", result.Value.Actions[2].PackageId);
    }

    [Fact]
    public void Plan_FeatureSelected_PackageIncluded()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            [CreatePackage("Pkg1")],
            [new ManifestFeature("F1", "Feature 1", null, true, false, ["Pkg1"])]);

        var selections = new Dictionary<string, bool> { ["F1"] = true };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            featureSelections: selections);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
        Assert.Equal("Pkg1", result.Value.Actions[0].PackageId);
    }

    [Fact]
    public void Plan_FeatureDeselected_PackageExcluded()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            [CreatePackage("Pkg1")],
            [new ManifestFeature("F1", "Feature 1", null, true, false, ["Pkg1"])]);

        var selections = new Dictionary<string, bool> { ["F1"] = false };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            featureSelections: selections);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Actions);
    }

    [Fact]
    public void Plan_PackageNotInFeature_AlwaysIncluded()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            [CreatePackage("Prereq"), CreatePackage("FeaturePkg")],
            [new ManifestFeature("F1", "Feature 1", null, true, false, ["FeaturePkg"])]);

        // Deselect the feature — only FeaturePkg should be excluded
        var selections = new Dictionary<string, bool> { ["F1"] = false };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            featureSelections: selections);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
        Assert.Equal("Prereq", result.Value.Actions[0].PackageId);
    }

    [Fact]
    public void Plan_FeatureSelected_ConditionFalse_PackageExcluded()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            [CreatePackage("Pkg1", "MissingVar")],
            [new ManifestFeature("F1", "Feature 1", null, true, false, ["Pkg1"])]);

        var selections = new Dictionary<string, bool> { ["F1"] = true };
        var variables = new VariableStore();

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            variables,
            featureSelections: selections);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Actions);
    }

    [Fact]
    public void Plan_MultipleFeatures_SamePackage_AnyFeatureSelected_PackageIncluded()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            [CreatePackage("SharedPkg")],
            [
                new ManifestFeature("F1", "Feature 1", null, true, false, ["SharedPkg"]),
                new ManifestFeature("F2", "Feature 2", null, true, false, ["SharedPkg"])
            ]);

        // Only F2 is selected, but SharedPkg is referenced by both — should be included
        var selections = new Dictionary<string, bool> { ["F1"] = false, ["F2"] = true };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            featureSelections: selections);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
        Assert.Equal("SharedPkg", result.Value.Actions[0].PackageId);
    }

    [Fact]
    public void Plan_RequiredFeature_PackageAlwaysIncluded()
    {
        // Required features are always marked as selected by the detection layer,
        // so as long as the selection dict has them as true, they pass the gate.
        var planner = new Planner();
        var manifest = CreateManifest(
            [CreatePackage("CorePkg")],
            [new ManifestFeature("Core", "Core", null, true, true, ["CorePkg"])]);

        // Simulate what the engine does: required features are always selected
        var selections = new Dictionary<string, bool> { ["Core"] = true };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            featureSelections: selections);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
        Assert.Equal("CorePkg", result.Value.Actions[0].PackageId);
    }

    [Fact]
    public void Plan_Uninstall_AllPackagesIncluded()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            [CreatePackage("Pkg1"), CreatePackage("Pkg2"), CreatePackage("Pkg3")],
            [
                new ManifestFeature("F1", "Feature 1", null, true, false, ["Pkg1"]),
                new ManifestFeature("F2", "Feature 2", null, true, false, ["Pkg2"])
            ]);

        // Even with deselected features, uninstall should include all packages
        var selections = new Dictionary<string, bool> { ["F1"] = false, ["F2"] = false };

        var detection = new DetectionResult(InstallState.Installed, "1.0.0", []);
        var result = planner.CreatePlan(
            manifest,
            detection,
            InstallAction.Uninstall,
            featureSelections: selections);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Actions.Count);
        Assert.All(result.Value.Actions, a => Assert.Equal(PlanActionType.Uninstall, a.ActionType));
        // Reverse order
        Assert.Equal("Pkg3", result.Value.Actions[0].PackageId);
        Assert.Equal("Pkg2", result.Value.Actions[1].PackageId);
        Assert.Equal("Pkg1", result.Value.Actions[2].PackageId);
    }
}
