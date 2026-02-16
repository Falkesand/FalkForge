namespace FalkForge.Engine.Tests.Planning;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;
using Xunit;

public sealed class PlannerConditionTests
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
    public void Package_WithNoCondition_IsAlwaysIncluded()
    {
        var planner = new Planner();
        var manifest = CreateManifest(CreatePackage("Pkg1"));
        var variables = new VariableStore();

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install, variables);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
        Assert.Equal("Pkg1", result.Value.Actions[0].PackageId);
    }

    [Fact]
    public void Package_WithTrueCondition_IsIncluded()
    {
        var planner = new Planner();
        var manifest = CreateManifest(CreatePackage("Pkg1", "MyVar"));
        var variables = new VariableStore();
        variables.Set("MyVar", "1");

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install, variables);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
        Assert.Equal("Pkg1", result.Value.Actions[0].PackageId);
    }

    [Fact]
    public void Package_WithFalseCondition_IsSkipped()
    {
        var planner = new Planner();
        var manifest = CreateManifest(CreatePackage("Pkg1", "MissingVar"));
        var variables = new VariableStore();

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install, variables);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Actions);
    }

    [Fact]
    public void Package_WithInvalidCondition_ReturnsFailure()
    {
        var planner = new Planner();
        var manifest = CreateManifest(CreatePackage("Pkg1", "((( bad syntax"));
        var variables = new VariableStore();

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install, variables);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void MultiplePackages_WithMixedConditions_FiltersCorrectly()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            CreatePackage("Always", null),
            CreatePackage("Included", "Enabled"),
            CreatePackage("Excluded", "MissingVar"));

        var variables = new VariableStore();
        variables.Set("Enabled", "1");

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install, variables);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Actions.Count);
        Assert.Equal("Always", result.Value.Actions[0].PackageId);
        Assert.Equal("Included", result.Value.Actions[1].PackageId);
    }

    [Fact]
    public void Condition_ReferencingVariableStoreValues_EvaluatesCorrectly()
    {
        var planner = new Planner();
        var manifest = CreateManifest(CreatePackage("Pkg1", "VersionCheck >= 2"));
        var variables = new VariableStore();
        variables.Set("VersionCheck", 3L);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install, variables);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
    }

    [Fact]
    public void Condition_WithComparisonThatFails_SkipsPackage()
    {
        var planner = new Planner();
        var manifest = CreateManifest(CreatePackage("Pkg1", "VersionCheck >= 5"));
        var variables = new VariableStore();
        variables.Set("VersionCheck", 2L);

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install, variables);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Actions);
    }

    [Fact]
    public void Uninstall_WithCondition_FiltersInReverseOrder()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            CreatePackage("Pkg1", null),
            CreatePackage("Pkg2", "ShouldRemove"),
            CreatePackage("Pkg3", null));

        var variables = new VariableStore();
        variables.Set("ShouldRemove", "1");

        var detection = new DetectionResult(InstallState.Installed, "1.0.0", []);
        var result = planner.CreatePlan(manifest, detection, InstallAction.Uninstall, variables);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Actions.Count);
        // Reverse order
        Assert.Equal("Pkg3", result.Value.Actions[0].PackageId);
        Assert.Equal("Pkg2", result.Value.Actions[1].PackageId);
        Assert.Equal("Pkg1", result.Value.Actions[2].PackageId);
    }

    [Fact]
    public void Uninstall_WithFalseCondition_SkipsPackage()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            CreatePackage("Pkg1", null),
            CreatePackage("Pkg2", "MissingVar"),
            CreatePackage("Pkg3", null));

        var variables = new VariableStore();

        var detection = new DetectionResult(InstallState.Installed, "1.0.0", []);
        var result = planner.CreatePlan(manifest, detection, InstallAction.Uninstall, variables);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Actions.Count);
        Assert.Equal("Pkg3", result.Value.Actions[0].PackageId);
        Assert.Equal("Pkg1", result.Value.Actions[1].PackageId);
    }

    [Fact]
    public void NullVariableStore_IncludesAllPackages()
    {
        var planner = new Planner();
        var manifest = CreateManifest(
            CreatePackage("Pkg1", "SomeCondition"),
            CreatePackage("Pkg2", null));

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install, variables: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Actions.Count);
    }

    [Fact]
    public void Condition_WithAndLogic_EvaluatesCorrectly()
    {
        var planner = new Planner();
        var manifest = CreateManifest(CreatePackage("Pkg1", "A AND B"));
        var variables = new VariableStore();
        variables.Set("A", "1");
        variables.Set("B", "1");

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install, variables);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Actions);
    }

    [Fact]
    public void Condition_WithPartialAndLogic_SkipsPackage()
    {
        var planner = new Planner();
        var manifest = CreateManifest(CreatePackage("Pkg1", "A AND B"));
        var variables = new VariableStore();
        variables.Set("A", "1");
        // B is not set

        var result = planner.CreatePlan(manifest, NotInstalledDetection, InstallAction.Install, variables);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Actions);
    }
}
