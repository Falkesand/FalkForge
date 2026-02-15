namespace FalkInstaller.Engine.Tests.Planning;

using FalkInstaller.Engine.Detection;
using FalkInstaller.Engine.Planning;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Manifest;
using FalkInstaller.Engine.Tests.Mocks;
using Xunit;

public sealed class PlannerRelatedBundleTests
{
    private static DetectionResult NotInstalledDetection =>
        new(InstallState.NotInstalled, null, []);

    [Fact]
    public void CreatePlan_UpgradeRelation_PlansUninstallBeforeInstall()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages: [TestManifestFactory.CreateMsiPackage(id: "NewPkg")]);

        var detectedBundles = new List<RelatedBundleInfo>
        {
            new()
            {
                BundleId = "{OLD-BUNDLE-0000-0000-000000000000}",
                InstalledVersion = "1.0.0",
                Relation = RelatedBundleRelation.Upgrade,
                RegistryKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{OLD}"
            }
        };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            detectedRelatedBundles: detectedBundles);

        Assert.True(result.IsSuccess);
        var plan = result.Value;

        // First action should be uninstall of the related bundle
        Assert.True(plan.Actions.Count >= 2);
        Assert.Equal(PlanActionType.Uninstall, plan.Actions[0].ActionType);
        Assert.Contains("RelatedBundle_", plan.Actions[0].PackageId);

        // Second action should be install of the new package
        Assert.Equal(PlanActionType.Install, plan.Actions[1].ActionType);
        Assert.Equal("NewPkg", plan.Actions[1].PackageId);
    }

    [Fact]
    public void CreatePlan_DetectRelation_DoesNotPlanUninstall()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages: [TestManifestFactory.CreateMsiPackage(id: "Pkg1")]);

        var detectedBundles = new List<RelatedBundleInfo>
        {
            new()
            {
                BundleId = "{DETECT-ONLY-0000-0000-000000000000}",
                InstalledVersion = "1.0.0",
                Relation = RelatedBundleRelation.Detect
            }
        };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            detectedRelatedBundles: detectedBundles);

        Assert.True(result.IsSuccess);
        var plan = result.Value;

        // Only the install of Pkg1, no uninstall of the detect-only bundle
        Assert.Single(plan.Actions);
        Assert.Equal(PlanActionType.Install, plan.Actions[0].ActionType);
        Assert.Equal("Pkg1", plan.Actions[0].PackageId);
    }

    [Fact]
    public void CreatePlan_NoDetectedBundles_NoUninstallActions()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages: [TestManifestFactory.CreateMsiPackage(id: "Pkg1")]);

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            detectedRelatedBundles: null);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
        Assert.Equal(PlanActionType.Install, result.Value.Actions[0].ActionType);
    }

    [Fact]
    public void CreatePlan_MultipleUpgradeBundles_AllPlannedBeforeInstall()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages: [TestManifestFactory.CreateMsiPackage(id: "NewPkg")]);

        var detectedBundles = new List<RelatedBundleInfo>
        {
            new()
            {
                BundleId = "{BUNDLE-A-0000-0000-000000000000}",
                InstalledVersion = "1.0.0",
                Relation = RelatedBundleRelation.Upgrade
            },
            new()
            {
                BundleId = "{BUNDLE-B-0000-0000-000000000000}",
                InstalledVersion = "2.0.0",
                Relation = RelatedBundleRelation.Upgrade
            }
        };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            detectedRelatedBundles: detectedBundles);

        Assert.True(result.IsSuccess);
        var plan = result.Value;

        Assert.Equal(3, plan.Actions.Count);
        // First two should be uninstalls of related bundles
        Assert.Equal(PlanActionType.Uninstall, plan.Actions[0].ActionType);
        Assert.Equal(PlanActionType.Uninstall, plan.Actions[1].ActionType);
        // Last should be the install
        Assert.Equal(PlanActionType.Install, plan.Actions[2].ActionType);
        Assert.Equal("NewPkg", plan.Actions[2].PackageId);
    }

    [Fact]
    public void CreatePlan_AddonRelation_DoesNotPlanUninstall()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages: [TestManifestFactory.CreateMsiPackage(id: "Pkg1")]);

        var detectedBundles = new List<RelatedBundleInfo>
        {
            new()
            {
                BundleId = "{ADDON-BUNDLE-0000-0000-000000000000}",
                InstalledVersion = "1.0.0",
                Relation = RelatedBundleRelation.Addon
            }
        };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            detectedRelatedBundles: detectedBundles);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
        Assert.Equal(PlanActionType.Install, result.Value.Actions[0].ActionType);
    }

    [Fact]
    public void CreatePlan_PatchRelation_DoesNotPlanUninstall()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages: [TestManifestFactory.CreateMsiPackage(id: "Pkg1")]);

        var detectedBundles = new List<RelatedBundleInfo>
        {
            new()
            {
                BundleId = "{PATCH-BUNDLE-0000-0000-000000000000}",
                InstalledVersion = "1.0.0",
                Relation = RelatedBundleRelation.Patch
            }
        };

        var result = planner.CreatePlan(
            manifest,
            NotInstalledDetection,
            InstallAction.Install,
            detectedRelatedBundles: detectedBundles);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
        Assert.Equal(PlanActionType.Install, result.Value.Actions[0].ActionType);
    }
}
