namespace FalkForge.Engine.Tests.Planning;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

public sealed class PrerequisitePlannerTests
{
    private static DetectionResult NotInstalledDetection =>
        new(InstallState.NotInstalled, null, []);

    private static PackageInfo CreatePackage(string id, bool isPrerequisite = false, bool vital = true)
    {
        return new PackageInfo
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Package {id}",
            SourcePath = $@"C:\test\{id}.msi",
            Sha256Hash = "AABBCCDD",
            Vital = vital,
            IsPrerequisite = isPrerequisite
        };
    }

    private static InstallerManifest CreateManifest(params PackageInfo[] packages)
    {
        return new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = packages
        };
    }

    [Fact]
    public void Plan_WithPrereqs_PrereqsOrderedFirst()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            CreatePackage("MainApp"),
            CreatePackage("Runtime", isPrerequisite: true),
            CreatePackage("Addon"));

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(3, plan.Actions.Count);
        // Prereq must come first, regardless of manifest order
        Assert.Equal("Runtime", plan.Actions[0].PackageId);
        Assert.Equal("MainApp", plan.Actions[1].PackageId);
        Assert.Equal("Addon", plan.Actions[2].PackageId);
    }

    [Fact]
    public void Plan_PrereqAlreadyInstalled_Skipped()
    {
        var planner = new Planner();
        var prereqPkg = CreatePackage("Runtime", isPrerequisite: true);
        var manifest = CreateManifest(
            prereqPkg,
            CreatePackage("MainApp"));

        // Detection reports the prerequisite package as already installed
        var packageStates = new Dictionary<string, InstallState>
        {
            ["Runtime"] = InstallState.Installed
        };

        var result = planner.CreatePlan(
            manifest, NotInstalledDetection, InstallAction.Install,
            detectedPackageStates: packageStates);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Single(plan.Actions);
        Assert.Equal("MainApp", plan.Actions[0].PackageId);
    }

    [Fact]
    public void Plan_PrereqsImplicitlyVital()
    {
        var planner = new Planner();
        // Explicitly set vital=false on a prereq — it should still be treated as vital
        var manifest = CreateManifest(
            CreatePackage("Runtime", isPrerequisite: true, vital: false),
            CreatePackage("MainApp"));

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal("Runtime", plan.Actions[0].PackageId);
        // The planned action's package should reflect Vital=true
        Assert.True(plan.Actions[0].Package.Vital);
    }

    [Fact]
    public void Plan_NoPrereqs_UnchangedBehavior()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            CreatePackage("Pkg1"),
            CreatePackage("Pkg2"),
            CreatePackage("Pkg3"));

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(3, plan.Actions.Count);
        // Original ordering preserved when no prereqs
        Assert.Equal("Pkg1", plan.Actions[0].PackageId);
        Assert.Equal("Pkg2", plan.Actions[1].PackageId);
        Assert.Equal("Pkg3", plan.Actions[2].PackageId);
    }
}
