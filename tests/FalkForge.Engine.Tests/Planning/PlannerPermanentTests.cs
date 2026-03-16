namespace FalkForge.Engine.Tests.Planning;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class PlannerPermanentTests
{
    [Fact]
    public void CreatePlan_Uninstall_SkipsPermanentPackages()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages:
            [
                TestManifestFactory.CreateMsiPackage(id: "Pkg1"),
                CreatePermanentPackage("PermanentPkg"),
                TestManifestFactory.CreateMsiPackage(id: "Pkg3")
            ]);

        var detection = new DetectionResult(InstallState.Installed, "1.0.0", []);
        var result = planner.CreatePlan(manifest, detection, InstallAction.Uninstall);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(2, plan.Actions.Count);
        Assert.All(plan.Actions, a => Assert.NotEqual("PermanentPkg", a.PackageId));
    }

    [Fact]
    public void CreatePlan_Install_IncludesPermanentPackages()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages:
            [
                TestManifestFactory.CreateMsiPackage(id: "Pkg1"),
                CreatePermanentPackage("PermanentPkg")
            ]);

        var result = planner.CreatePlan(
            manifest,
            new DetectionResult(InstallState.NotInstalled, null, []),
            InstallAction.Install);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Actions.Count);
        Assert.Contains(result.Value.Actions, a => a.PackageId == "PermanentPkg");
    }

    [Fact]
    public void CreatePlan_Repair_IncludesPermanentPackages()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages:
            [
                TestManifestFactory.CreateMsiPackage(id: "Pkg1"),
                CreatePermanentPackage("PermanentPkg")
            ]);

        var detection = new DetectionResult(InstallState.Installed, "1.0.0", []);
        var result = planner.CreatePlan(manifest, detection, InstallAction.Repair);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Actions.Count);
        Assert.Contains(result.Value.Actions, a => a.PackageId == "PermanentPkg");
    }

    private static PackageInfo CreatePermanentPackage(string id)
    {
        return new PackageInfo
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Permanent Package ({id})",
            SourcePath = $@"C:\test\{id}.msi",
            Sha256Hash = "AABBCCDD",
            Permanent = true
        };
    }
}
