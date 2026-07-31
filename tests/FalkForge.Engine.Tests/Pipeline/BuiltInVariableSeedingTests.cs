namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Proves <see cref="BuiltInVariables.Populate"/> is actually reachable from the production
/// pipeline. Before this fix, <c>BuiltInVariables.Populate</c> was never called anywhere —
/// <see cref="EngineSession.BindToPipe"/> constructed a bare <c>new VariableStore()</c> and
/// handed it straight to the pipeline unseeded. A unit test that calls
/// <c>BuiltInVariables.Populate</c> directly cannot catch that defect (it would still pass
/// against the never-wired production code), so every test here drives the real
/// <see cref="InstallerPipelineBuilder"/> → <see cref="IInstallerPipeline"/> path (Detect then
/// Plan) instead.
/// </summary>
public sealed class BuiltInVariableSeedingTests
{
    private static InstallerManifest ManifestWithCondition(string installCondition) =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages =
            [
                new PackageInfo
                {
                    Id = "Pkg1",
                    Type = PackageType.ExePackage,
                    DisplayName = "Test Pkg1",
                    SourcePath = @"C:\fake\Pkg1.exe",
                    Sha256Hash = "DEADBEEF",
                    InstallCondition = installCondition,
                    Properties = new Dictionary<string, string>
                    {
                        ["InstallArguments"] = "/quiet",
                        ["UninstallArguments"] = "/quiet"
                    }
                }
            ]
        };

    private static UiRequest.Plan InstallRequest() =>
        new(
            InstallAction.Install,
            InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, FalkForge.SensitiveBytes>());

    [Fact]
    public async Task Pipeline_PackageConditionOnBuiltInVersionNT_IsPlanned()
    {
        // VersionNT is always >= 6.0 on any supported Windows/.NET host. Before the fix,
        // ResolveVariable saw no "VersionNT" entry in the store at all, the comparison of ""
        // against "6.0" was false, and Planner.AddPackagesForward silently dropped the package.
        var manifest = ManifestWithCondition("VersionNT >= 6.0");
        var registry = new MockRegistry();
        var store = new VariableStore();
        await using var channel = new FakeUiChannel();

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(registry)
            .WithVariableStore(store)
            .WithPlatformServices(new FakePlatformServices(registry))
            .WithUiChannel(channel)
            .Build();

        var detectResult = await pipeline.DetectAsync(CancellationToken.None);
        Assert.True(detectResult.IsSuccess);

        var planResult = await pipeline.PlanAsync(InstallRequest(), CancellationToken.None);
        Assert.True(planResult.IsSuccess, planResult.IsFailure ? planResult.Error.Message : null);

        Assert.Contains(planResult.Value.Actions, a => a.PackageId == "Pkg1");
    }

    [Fact]
    public async Task Pipeline_PackageConditionOnPrivileged_IsPlanned()
    {
        // Machine-independent by construction (true whether Privileged resolves to 0 or 1) — this
        // isolates "the variable exists in the store at all" from "the variable's resolved value".
        var manifest = ManifestWithCondition("Privileged = 0 OR Privileged = 1");
        var registry = new MockRegistry();
        var store = new VariableStore();
        await using var channel = new FakeUiChannel();

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(registry)
            .WithVariableStore(store)
            .WithPlatformServices(new FakePlatformServices(registry))
            .WithUiChannel(channel)
            .Build();

        var detectResult = await pipeline.DetectAsync(CancellationToken.None);
        Assert.True(detectResult.IsSuccess);

        var planResult = await pipeline.PlanAsync(InstallRequest(), CancellationToken.None);
        Assert.True(planResult.IsSuccess, planResult.IsFailure ? planResult.Error.Message : null);

        Assert.Contains(planResult.Value.Actions, a => a.PackageId == "Pkg1");
    }

    [Fact]
    public async Task Pipeline_AfterDetect_StoreContainsBuiltIns()
    {
        var manifest = ManifestWithCondition(installCondition: "");
        var registry = new MockRegistry();
        var store = new VariableStore();
        var fakeEnvironment = new FakeEnvironment { MachineName = "PIPELINE-FAKE-HOST" };
        await using var channel = new FakeUiChannel();

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(registry)
            .WithVariableStore(store)
            .WithPlatformServices(new FakePlatformServices(registry, fakeEnvironment))
            .WithUiChannel(channel)
            .Build();

        await pipeline.DetectAsync(CancellationToken.None);

        Assert.True(store.Contains(BuiltInVariableNames.VersionNT));
        Assert.True(store.Contains(BuiltInVariableNames.ComputerName));

        // Proves IPlatformServices was actually plumbed through to BuiltInVariables.Populate
        // rather than a null-platform OS fallback silently running instead.
        var computerName = store.GetString(BuiltInVariableNames.ComputerName);
        Assert.True(computerName.IsSuccess);
        Assert.Equal("PIPELINE-FAKE-HOST", computerName.Value);
    }

    [Fact]
    public async Task Pipeline_ClockIsInjected_DateMatchesFakeClock()
    {
        var manifest = ManifestWithCondition(installCondition: "");
        var registry = new MockRegistry();
        var store = new VariableStore();
        var fakeClock = new FakeClock(new DateTimeOffset(2031, 9, 2, 0, 0, 0, TimeSpan.Zero));
        await using var channel = new FakeUiChannel();

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(registry)
            .WithVariableStore(store)
            .WithPlatformServices(new FakePlatformServices(registry))
            .WithClock(fakeClock)
            .WithUiChannel(channel)
            .Build();

        await pipeline.DetectAsync(CancellationToken.None);

        var date = store.GetString(BuiltInVariableNames.Date);
        Assert.True(date.IsSuccess);
        Assert.Equal("20310902", date.Value);
    }
}
