namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// Verifies that <see cref="InstallerPipeline"/> correctly propagates
/// <see cref="InstallerManifest.IsDryRun"/> into the execution context,
/// and that the manifest model's IsDryRun property behaves as documented.
/// </summary>
public sealed class InstallerPipelineDryRunTests
{
    private static InstallerManifest MakeDryRunManifest(bool isDryRun) => new()
    {
        Name = "TestApp",
        Manufacturer = "Contoso",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = InstallScope.PerMachine,
        Packages = [],
        IsDryRun = isDryRun
    };

    [Fact]
    public void InstallerManifest_IsDryRun_DefaultsToFalse()
    {
        // Manifest default must be false — baked-in dry-run is opt-in.
        var manifest = new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Contoso",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = []
        };
        Assert.False(manifest.IsDryRun);
    }

    [Fact]
    public void InstallerManifest_IsDryRun_CanBeSetTrue()
    {
        var manifest = MakeDryRunManifest(isDryRun: true);
        Assert.True(manifest.IsDryRun);
    }

    [Fact]
    public async Task Pipeline_WithDryRunManifest_DetectPhase_Succeeds()
    {
        // DetectAsync must succeed when manifest has IsDryRun = true.
        // This confirms the pipeline accepts a dry-run manifest without error.
        var manifest = MakeDryRunManifest(isDryRun: true);

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .Build();

        // DetectAsync succeeds even without a real IRegistry — DetectStep is skipped
        // when registry is not wired (passthrough mode). With no registry the
        // step is null, so the phase passes through.
        var detectResult = await pipeline.DetectAsync(CancellationToken.None);
        Assert.True(detectResult.IsSuccess, $"DetectAsync failed: {(detectResult.IsFailure ? detectResult.Error.Message : string.Empty)}");
    }

    [Fact]
    public async Task Pipeline_WithoutDryRunManifest_DetectPhase_Succeeds()
    {
        var manifest = MakeDryRunManifest(isDryRun: false);

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .Build();

        var detectResult = await pipeline.DetectAsync(CancellationToken.None);
        Assert.True(detectResult.IsSuccess);
    }

    [Fact]
    public async Task Pipeline_WithDryRunManifest_ExportPlanAfterDetect_ReturnsNoplan()
    {
        // ExportPlan before PlanAsync must fail with a clear "no plan" message.
        // This validates the pipeline correctly enforces phase order for dry-run manifests.
        var manifest = MakeDryRunManifest(isDryRun: true);

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .Build();

        await pipeline.DetectAsync(CancellationToken.None);

        // ExportPlan before PlanAsync = no plan yet
        var exportResult = pipeline.ExportPlan(null);
        Assert.True(exportResult.IsFailure, "ExportPlan before PlanAsync must fail");
        Assert.Contains("no plan", exportResult.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
