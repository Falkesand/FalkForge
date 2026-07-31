namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Engine.Variables;
using FalkForge.Testing;
using Xunit;

using MockRegistry = FalkForge.Testing.MockRegistry;

/// <summary>
/// Defect C: <see cref="EngineSession.BindToPipe"/> built <c>MsiExecutor</c> with a
/// <c>static () =&gt; null</c> variable-store accessor and <c>ExeExecutor</c> via its one-arg
/// constructor (which also defaults the accessor to null) — so <c>[VARIABLE]</c> references in
/// MSI properties and EXE arguments were NEVER expanded in production, even after the built-in
/// seeding fix landed (a separate commit) made the store non-empty. This test drives a full
/// Detect → Plan → Apply pipeline with an EXE package whose <c>InstallArguments</c> references a
/// built-in variable, and asserts the process runner actually received the EXPANDED string —
/// proving the executor was constructed with a live accessor into the SAME <see cref="VariableStore"/>
/// the pipeline seeds, not a null one.
/// </summary>
public sealed class ExeExecutorVariableWiringTests
{
    private static InstallerManifest ManifestWithExeArguments(string installArguments) =>
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
                    Properties = new Dictionary<string, string>
                    {
                        ["InstallArguments"] = installArguments,
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
    public async Task Apply_ExeInstallArgumentsReferenceSeededBuiltIn_ProcessRunnerReceivesExpandedArguments()
    {
        var manifest = ManifestWithExeArguments("/computer=[ComputerName] /quiet");
        var registry = new MockRegistry();
        var store = new VariableStore();
        var fakeEnvironment = new FakeEnvironment { MachineName = "EXPAND-VAR-HOST" };
        var runner = new MockProcessRunner().WithExitCode(0);
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        // Mirrors exactly how EngineSession.BindToPipe wires MsiExecutor/ExeExecutor after the
        // fix: both share the SAME VariableStore instance registered via WithVariableStore below.
        // Mirrors exactly how EngineSession.BindToPipe wires MsiExecutor/ExeExecutor after the
        // fix: both share the SAME VariableStore instance registered via WithVariableStore below.
        var packageExecutor = new PackageExecutor(
            new MsiExecutor(static () => null, () => store),
            new MsuExecutor(runner),
            new MspExecutor(runner),
            new BundleExecutor(runner),
            new ExeExecutor(runner, () => store),
            new NetRuntimeExecutor(runner));

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(registry)
            .WithVariableStore(store)
            .WithPlatformServices(new FakePlatformServices(registry, fakeEnvironment))
            .WithPackageExecutor(packageExecutor)
            .WithJournalStore(journalStore)
            .WithUiChannel(channel)
            .Build();

        var detectResult = await pipeline.DetectAsync(CancellationToken.None);
        Assert.True(detectResult.IsSuccess);

        var planResult = await pipeline.PlanAsync(InstallRequest(), CancellationToken.None);
        Assert.True(planResult.IsSuccess, planResult.IsFailure ? planResult.Error.Message : null);

        var applyResult = await pipeline.ApplyAsync(CancellationToken.None);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);

        Assert.Equal("/computer=EXPAND-VAR-HOST /quiet", runner.LastArguments);
    }
}
