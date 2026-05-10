namespace FalkForge.Engine.Tests.Pipeline;

using System.Runtime.InteropServices;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Verifies Fix 6: PlanStep must reject packages whose required architecture is
/// incompatible with the host OS, returning <see cref="ErrorKind.ArchitectureMismatch"/>
/// at plan time rather than surfacing MSI error 1603 at install time.
///
/// <para>Allowed combinations:</para>
/// <list type="bullet">
///   <item>x64 on x64 — native match</item>
///   <item>x86 on x64 — WoW64 allowed</item>
///   <item>x86 on Arm64 — x86 emulation allowed</item>
///   <item>x64 on Arm64 — x64 emulation allowed</item>
///   <item>Neutral (Any) on any host — always allowed</item>
/// </list>
/// </summary>
public sealed class ArchitectureMismatchTests
{
    private static UiRequest.Plan InstallRequest() =>
        new(
            InstallAction.Install,
            InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>());

    private static InstallerManifest ManifestWith(params PackageInfo[] packages) =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = packages
        };

    private static PackageInfo MsiPackageWithArch(string id, PackageArchitecture arch) =>
        new()
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Test {id}",
            SourcePath = $@"C:\fake\{id}.msi",
            Sha256Hash = "DEADBEEF",
            Architecture = arch
        };

    // ──────────────────────────────────────────────────────────────────────────
    // Mismatch cases (blocked at plan time)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanStep_X64Package_On_X86Host_ReturnsArchitectureMismatch()
    {
        var channel = new FakeUiChannel();
        var ctx = PipelineContextFactory.WithDetection(
            hostArchitecture: Architecture.X86);

        var manifest = ManifestWith(MsiPackageWithArch("App", PackageArchitecture.X64));
        ctx.Manifest = manifest;

        var step = new PlanStep(
            new FalkForge.Engine.Planning.Planner(),
            channel,
            hostArchitecture: Architecture.X86);

        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ArchitectureMismatch, result.Error.Kind);
        Assert.Contains("App", result.Error.Message);
        Assert.Contains("x64", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanStep_Arm64Package_On_X64Host_ReturnsArchitectureMismatch()
    {
        var channel = new FakeUiChannel();
        var ctx = PipelineContextFactory.WithDetection(
            hostArchitecture: Architecture.X64);

        var manifest = ManifestWith(MsiPackageWithArch("ArmApp", PackageArchitecture.Arm64));
        ctx.Manifest = manifest;

        var step = new PlanStep(
            new FalkForge.Engine.Planning.Planner(),
            channel,
            hostArchitecture: Architecture.X64);

        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ArchitectureMismatch, result.Error.Kind);
        Assert.Contains("ArmApp", result.Error.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Allowed cases (WoW64 and Windows-on-Arm emulation)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanStep_X86Package_On_X64Host_IsAllowed_WoW64()
    {
        var channel = new FakeUiChannel();
        var ctx = PipelineContextFactory.WithDetection(
            hostArchitecture: Architecture.X64);

        var manifest = ManifestWith(MsiPackageWithArch("x86App", PackageArchitecture.X86));
        ctx.Manifest = manifest;

        var step = new PlanStep(
            new FalkForge.Engine.Planning.Planner(),
            channel,
            hostArchitecture: Architecture.X64);

        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        // x86 on x64 is WoW64 — must not return ArchitectureMismatch
        Assert.True(result.IsSuccess || result.Error.Kind != ErrorKind.ArchitectureMismatch,
            $"x86 on x64 must be allowed (WoW64). Got: {(result.IsFailure ? result.Error : "success")}");
    }

    [Fact]
    public async Task PlanStep_X86Package_On_Arm64Host_IsAllowed_Emulation()
    {
        var channel = new FakeUiChannel();
        var ctx = PipelineContextFactory.WithDetection(
            hostArchitecture: Architecture.Arm64);

        var manifest = ManifestWith(MsiPackageWithArch("x86App", PackageArchitecture.X86));
        ctx.Manifest = manifest;

        var step = new PlanStep(
            new FalkForge.Engine.Planning.Planner(),
            channel,
            hostArchitecture: Architecture.Arm64);

        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess || result.Error.Kind != ErrorKind.ArchitectureMismatch,
            $"x86 on Arm64 must be allowed (emulation). Got: {(result.IsFailure ? result.Error : "success")}");
    }

    [Fact]
    public async Task PlanStep_X64Package_On_Arm64Host_IsAllowed_Emulation()
    {
        var channel = new FakeUiChannel();
        var ctx = PipelineContextFactory.WithDetection(
            hostArchitecture: Architecture.Arm64);

        var manifest = ManifestWith(MsiPackageWithArch("x64App", PackageArchitecture.X64));
        ctx.Manifest = manifest;

        var step = new PlanStep(
            new FalkForge.Engine.Planning.Planner(),
            channel,
            hostArchitecture: Architecture.Arm64);

        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess || result.Error.Kind != ErrorKind.ArchitectureMismatch,
            $"x64 on Arm64 must be allowed (emulation). Got: {(result.IsFailure ? result.Error : "success")}");
    }

    [Fact]
    public async Task PlanStep_NeutralPackage_On_AnyHost_IsAlwaysAllowed()
    {
        var channel = new FakeUiChannel();
        var ctx = PipelineContextFactory.WithDetection(
            hostArchitecture: Architecture.X86);

        var manifest = ManifestWith(MsiPackageWithArch("NeutralApp", PackageArchitecture.Neutral));
        ctx.Manifest = manifest;

        var step = new PlanStep(
            new FalkForge.Engine.Planning.Planner(),
            channel,
            hostArchitecture: Architecture.X86);

        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess || result.Error.Kind != ErrorKind.ArchitectureMismatch,
            $"Neutral package must be allowed on any host. Got: {(result.IsFailure ? result.Error : "success")}");
    }

    [Fact]
    public async Task PlanStep_X64Package_On_X64Host_NativeMatch_IsAllowed()
    {
        var channel = new FakeUiChannel();
        var ctx = PipelineContextFactory.WithDetection(
            hostArchitecture: Architecture.X64);

        var manifest = ManifestWith(MsiPackageWithArch("x64App", PackageArchitecture.X64));
        ctx.Manifest = manifest;

        var step = new PlanStep(
            new FalkForge.Engine.Planning.Planner(),
            channel,
            hostArchitecture: Architecture.X64);

        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess || result.Error.Kind != ErrorKind.ArchitectureMismatch,
            $"x64 on x64 must be allowed (native). Got: {(result.IsFailure ? result.Error : "success")}");
    }
}

/// <summary>
/// Factory for creating <see cref="PipelineContext"/> pre-seeded with detection results,
/// used in tests that need to skip past DetectStep.
/// </summary>
internal static class PipelineContextFactory
{
    public static PipelineContext WithDetection(
        Architecture hostArchitecture = Architecture.X64,
        InstallState state = InstallState.NotInstalled)
    {
        return new PipelineContext
        {
            Detection = new FalkForge.Engine.Detection.DetectionResult(
                State: state,
                CurrentVersion: null,
                Features: [])
        };
    }
}
