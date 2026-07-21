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
    public void Plan_PackageFeatureSelection_StampsAddLocalOnMatchingActionOnly()
    {
        // Per-package interactive MSI feature selection stamps ADDLOCAL only on the
        // matching package's action — and does NOT gate any package in/out (that is the
        // separate bundle-level featureSelections concern).
        var planner = new Planner();
        var manifest = CreateManifest([CreatePackage("Pkg1"), CreatePackage("Pkg2")]);

        var selections = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Pkg1"] = ["FeatureA", "FeatureB"],
        };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            packageFeatureSelections: selections);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Actions.Count);
        var pkg1 = result.Value.Actions.Single(a => a.PackageId == "Pkg1");
        var pkg2 = result.Value.Actions.Single(a => a.PackageId == "Pkg2");
        Assert.Equal("FeatureA,FeatureB", pkg1.Properties["ADDLOCAL"]);
        Assert.False(pkg2.Properties.ContainsKey("ADDLOCAL"));
    }

    [Fact]
    public void Plan_PackageFeatureSelection_OverridesStaticAddLocalFromUserProperties()
    {
        // A static ADDLOCAL (arriving via user properties) must lose to an interactive
        // per-package selection for that package.
        var planner = new Planner();
        var manifest = CreateManifest([CreatePackage("Pkg1")]);

        var userProps = new Dictionary<string, string> { ["ADDLOCAL"] = "StaticFeature" };
        var selections = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Pkg1"] = ["Interactive"],
        };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            userProperties: userProps,
            packageFeatureSelections: selections);

        Assert.True(result.IsSuccess);
        var pkg1 = result.Value.Actions.Single();
        Assert.Equal("Interactive", pkg1.Properties["ADDLOCAL"]);
    }

    [Fact]
    public void Plan_PackageFeatureSelection_DoesNotAffectBundleFeatureGating()
    {
        // Guard the separation: providing a per-package ADDLOCAL selection for a
        // feature-gated package must not itself select the package — bundle-level
        // featureSelections still decides whether the package installs at all.
        var planner = new Planner();
        var manifest = CreateManifest(
            [CreatePackage("Pkg1")],
            [new ManifestFeature("F1", "Feature 1", null, true, false, ["Pkg1"])]);

        // F1 deselected → Pkg1 excluded, regardless of any per-package ADDLOCAL selection.
        var featureSelections = new Dictionary<string, bool> { ["F1"] = false };
        var packageSelections = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Pkg1"] = ["FeatureA"],
        };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            featureSelections: featureSelections,
            packageFeatureSelections: packageSelections);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Actions);
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
