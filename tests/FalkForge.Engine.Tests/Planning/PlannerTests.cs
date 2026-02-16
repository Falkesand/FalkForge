namespace FalkForge.Engine.Tests.Planning;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class PlannerTests
{
    private static DetectionResult NotInstalledDetection =>
        new(InstallState.NotInstalled, null, []);

    [Fact]
    public void CreatePlan_InstallAction_CreatesInstallActionsForAllPackages()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages:
            [
                TestManifestFactory.CreateMsiPackage(id: "Pkg1"),
                TestManifestFactory.CreateMsiPackage(id: "Pkg2"),
                TestManifestFactory.CreateMsiPackage(id: "Pkg3")
            ]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(3, plan.Actions.Count);
        Assert.All(plan.Actions, a => Assert.Equal(PlanActionType.Install, a.ActionType));
        Assert.Equal("Pkg1", plan.Actions[0].PackageId);
        Assert.Equal("Pkg2", plan.Actions[1].PackageId);
        Assert.Equal("Pkg3", plan.Actions[2].PackageId);
    }

    [Fact]
    public void CreatePlan_UninstallAction_CreatesUninstallActionsInReverseOrder()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages:
            [
                TestManifestFactory.CreateMsiPackage(id: "Pkg1"),
                TestManifestFactory.CreateMsiPackage(id: "Pkg2"),
                TestManifestFactory.CreateMsiPackage(id: "Pkg3")
            ]);

        var detection = new DetectionResult(InstallState.Installed, "1.0.0", []);
        var result = planner.CreatePlan(manifest, detection, InstallAction.Uninstall);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(3, plan.Actions.Count);
        Assert.All(plan.Actions, a => Assert.Equal(PlanActionType.Uninstall, a.ActionType));
        // Reverse order
        Assert.Equal("Pkg3", plan.Actions[0].PackageId);
        Assert.Equal("Pkg2", plan.Actions[1].PackageId);
        Assert.Equal("Pkg1", plan.Actions[2].PackageId);
    }

    [Fact]
    public void CreatePlan_RepairAction_CreatesRepairActions()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages:
            [
                TestManifestFactory.CreateMsiPackage(id: "Pkg1"),
                TestManifestFactory.CreateMsiPackage(id: "Pkg2")
            ]);

        var detection = new DetectionResult(InstallState.Installed, "1.0.0", []);
        var result = planner.CreatePlan(manifest, detection, InstallAction.Repair);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(2, plan.Actions.Count);
        Assert.All(plan.Actions, a => Assert.Equal(PlanActionType.Repair, a.ActionType));
    }

    [Fact]
    public void CreatePlan_ModifyAction_CreatesInstallActions()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages: [TestManifestFactory.CreateMsiPackage(id: "Pkg1")]);

        var detection = new DetectionResult(InstallState.Installed, "1.0.0", []);
        var result = planner.CreatePlan(manifest, detection, InstallAction.Modify);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
        Assert.Equal(PlanActionType.Install, result.Value.Actions[0].ActionType);
    }

    [Fact]
    public void CreatePlan_UnknownAction_ReturnsFailure()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple();

        var result = planner.CreatePlan(manifest, NotInstalledDetection, (InstallAction)99);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PlanningError, result.Error.Kind);
    }

    [Fact]
    public void CreatePlan_EmptyPackages_ReturnsEmptyPlan()
    {
        var planner = new Planner();
        var manifest = new FalkForge.Engine.Protocol.Manifest.InstallerManifest
        {
            Name = "EmptyApp",
            Manufacturer = "Test",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = []
        };

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Actions);
    }

    [Fact]
    public void CreatePlan_InstallAction_SetsPackageReferenceOnActions()
    {
        var planner = new Planner();
        var package = TestManifestFactory.CreateMsiPackage(id: "Pkg1");
        var manifest = TestManifestFactory.CreateSimple(packages: [package]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        Assert.Same(package, result.Value.Actions[0].Package);
    }
}
