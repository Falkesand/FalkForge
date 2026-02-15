namespace FalkInstaller.Engine.Tests.Planning;

using FalkInstaller.Engine.Detection;
using FalkInstaller.Engine.Planning;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Manifest;
using FalkInstaller.Engine.Tests.Mocks;
using Xunit;

public sealed class RollbackSegmentTests
{
    private static DetectionResult NotInstalledDetection =>
        new(InstallState.NotInstalled, null, []);

    private static InstallerManifest CreateManifestWithChain(
        PackageInfo[] packages,
        ManifestChainItem[] chain)
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
            Chain = chain
        };
    }

    [Fact]
    public void NoBoundaries_CreatesSingleSegmentWithAllActions()
    {
        var planner = new Planner();
        var pkg1 = TestManifestFactory.CreateMsiPackage(id: "Pkg1");
        var pkg2 = TestManifestFactory.CreateMsiPackage(id: "Pkg2");
        var pkg3 = TestManifestFactory.CreateMsiPackage(id: "Pkg3");

        var manifest = CreateManifestWithChain(
            [pkg1, pkg2, pkg3],
            [
                new PackageManifestChainItem(pkg1),
                new PackageManifestChainItem(pkg2),
                new PackageManifestChainItem(pkg3)
            ]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Single(plan.Segments);
        Assert.Equal("__default__", plan.Segments[0].BoundaryId);
        Assert.Equal(3, plan.Segments[0].Actions.Count);
    }

    [Fact]
    public void EmptyChain_CreatesSingleSegmentWithAllActions()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages:
            [
                TestManifestFactory.CreateMsiPackage(id: "Pkg1"),
                TestManifestFactory.CreateMsiPackage(id: "Pkg2")
            ]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Single(plan.Segments);
        Assert.Equal("__default__", plan.Segments[0].BoundaryId);
        Assert.Equal(2, plan.Segments[0].Actions.Count);
    }

    [Fact]
    public void OneBoundary_CreatesTwoSegments()
    {
        var planner = new Planner();
        var pkg1 = TestManifestFactory.CreateMsiPackage(id: "Pkg1");
        var pkg2 = TestManifestFactory.CreateMsiPackage(id: "Pkg2");
        var pkg3 = TestManifestFactory.CreateMsiPackage(id: "Pkg3");

        var manifest = CreateManifestWithChain(
            [pkg1, pkg2, pkg3],
            [
                new PackageManifestChainItem(pkg1),
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB1" }),
                new PackageManifestChainItem(pkg2),
                new PackageManifestChainItem(pkg3)
            ]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(2, plan.Segments.Count);
    }

    [Fact]
    public void MultipleBoundaries_CreateMultipleSegments()
    {
        var planner = new Planner();
        var pkg1 = TestManifestFactory.CreateMsiPackage(id: "Pkg1");
        var pkg2 = TestManifestFactory.CreateMsiPackage(id: "Pkg2");
        var pkg3 = TestManifestFactory.CreateMsiPackage(id: "Pkg3");

        var manifest = CreateManifestWithChain(
            [pkg1, pkg2, pkg3],
            [
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB1" }),
                new PackageManifestChainItem(pkg1),
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB2" }),
                new PackageManifestChainItem(pkg2),
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB3" }),
                new PackageManifestChainItem(pkg3)
            ]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(3, plan.Segments.Count);
        Assert.Equal("RB1", plan.Segments[0].BoundaryId);
        Assert.Equal("RB2", plan.Segments[1].BoundaryId);
        Assert.Equal("RB3", plan.Segments[2].BoundaryId);
    }

    [Fact]
    public void ActionsAssignedToCorrectSegments()
    {
        var planner = new Planner();
        var pkg1 = TestManifestFactory.CreateMsiPackage(id: "Pkg1");
        var pkg2 = TestManifestFactory.CreateMsiPackage(id: "Pkg2");
        var pkg3 = TestManifestFactory.CreateMsiPackage(id: "Pkg3");

        var manifest = CreateManifestWithChain(
            [pkg1, pkg2, pkg3],
            [
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB1" }),
                new PackageManifestChainItem(pkg1),
                new PackageManifestChainItem(pkg2),
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB2" }),
                new PackageManifestChainItem(pkg3)
            ]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(2, plan.Segments.Count);

        // First segment: Pkg1, Pkg2
        Assert.Equal(2, plan.Segments[0].Actions.Count);
        Assert.Equal("Pkg1", plan.Segments[0].Actions[0].PackageId);
        Assert.Equal("Pkg2", plan.Segments[0].Actions[1].PackageId);

        // Second segment: Pkg3
        Assert.Single(plan.Segments[1].Actions);
        Assert.Equal("Pkg3", plan.Segments[1].Actions[0].PackageId);
    }

    [Fact]
    public void SegmentVitalFlag_MatchesBoundary()
    {
        var planner = new Planner();
        var pkg1 = TestManifestFactory.CreateMsiPackage(id: "Pkg1");
        var pkg2 = TestManifestFactory.CreateMsiPackage(id: "Pkg2");

        var manifest = CreateManifestWithChain(
            [pkg1, pkg2],
            [
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB1", Vital = true }),
                new PackageManifestChainItem(pkg1),
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB2", Vital = false }),
                new PackageManifestChainItem(pkg2)
            ]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(2, plan.Segments.Count);
        Assert.True(plan.Segments[0].Vital);
        Assert.False(plan.Segments[1].Vital);
    }

    [Fact]
    public void EmptySegment_BoundaryWithNoFollowingPackages()
    {
        var planner = new Planner();
        var pkg1 = TestManifestFactory.CreateMsiPackage(id: "Pkg1");

        var manifest = CreateManifestWithChain(
            [pkg1],
            [
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB1" }),
                new PackageManifestChainItem(pkg1),
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB2" })
            ]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(2, plan.Segments.Count);
        Assert.Single(plan.Segments[0].Actions);
        Assert.Empty(plan.Segments[1].Actions);
    }

    [Fact]
    public void PackagesBeforeBoundary_GetDefaultSegment()
    {
        var planner = new Planner();
        var pkg1 = TestManifestFactory.CreateMsiPackage(id: "Pkg1");
        var pkg2 = TestManifestFactory.CreateMsiPackage(id: "Pkg2");

        var manifest = CreateManifestWithChain(
            [pkg1, pkg2],
            [
                new PackageManifestChainItem(pkg1),
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB1" }),
                new PackageManifestChainItem(pkg2)
            ]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal("__default__", plan.Segments[0].BoundaryId);
        Assert.True(plan.Segments[0].Vital);
        Assert.Single(plan.Segments[0].Actions);
        Assert.Equal("Pkg1", plan.Segments[0].Actions[0].PackageId);
        Assert.Equal("RB1", plan.Segments[1].BoundaryId);
        Assert.Single(plan.Segments[1].Actions);
        Assert.Equal("Pkg2", plan.Segments[1].Actions[0].PackageId);
    }

    [Fact]
    public void FlattenedActions_StillAvailable()
    {
        var planner = new Planner();
        var pkg1 = TestManifestFactory.CreateMsiPackage(id: "Pkg1");
        var pkg2 = TestManifestFactory.CreateMsiPackage(id: "Pkg2");

        var manifest = CreateManifestWithChain(
            [pkg1, pkg2],
            [
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB1" }),
                new PackageManifestChainItem(pkg1),
                new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "RB2" }),
                new PackageManifestChainItem(pkg2)
            ]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        // Flattened actions should still contain all
        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal("Pkg1", plan.Actions[0].PackageId);
        Assert.Equal("Pkg2", plan.Actions[1].PackageId);
    }

    [Fact]
    public void DefaultSegment_IsVital()
    {
        var planner = new Planner();
        var manifest = TestManifestFactory.CreateSimple(
            packages: [TestManifestFactory.CreateMsiPackage(id: "Pkg1")]);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Segments);
        Assert.True(result.Value.Segments[0].Vital);
    }
}
