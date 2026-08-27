namespace FalkForge.Engine.Tests;

using System.Reflection;
using System.Text.Json;
using FalkForge.Engine;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;          // IUiChannel
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Proves that <see cref="EngineSession.BindToPipe"/> — the production entry point, not a
/// hand-built copy of its wiring — gives <c>MsiExecutor</c> an elevation-client accessor that
/// resolves the gateway <c>ElevateStep</c> actually started, and resolves null when it started
/// none or when this engine process is already elevated.
/// <para>
/// Each test drives a real <see cref="PlanAction"/> through
/// <see cref="MsiExecutor.ExecuteAsync"/> rather than calling the accessor by hand, so a change
/// that stopped <c>ExecuteAsync</c> consulting the accessor would fail them.
/// </para>
/// <para>
/// Before this wiring existed, <c>BindToPipe</c> passed <c>static () =&gt; null</c>, every MSI
/// installed in-process from an unelevated engine, and Windows refused a per-machine package with
/// error 1925 ("You do not have sufficient privileges to complete this installation for all users
/// of the machine"). Measured on a real machine: <c>MsiInstallProductW</c> returned 1603, and the
/// same MSI with <c>ALLUSERS</c> cleared returned 0 and installed.
/// </para>
/// <para>
/// Two things must NOT regress while fixing that. A per-user bundle must keep installing
/// in-process: launching the companion for it raised a UAC prompt for nothing and, when the pipe
/// did not come back, ended a real per-user install with "Elevation failed: Pipe is broken" before
/// a single package was touched. And an engine the user started with "Run as administrator" must
/// keep installing in-process too: it already holds the privileges Windows wants, so sending the
/// install to a companion that demands a baked publisher key would refuse an install that works
/// today.
/// </para>
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class EngineSessionElevationWiringTests : IDisposable
{
    private readonly string _tempDir;

    public EngineSessionElevationWiringTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge_Tests_ElevationWiring", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => TestTemp.TryDelete(_tempDir);

    private static InstallerManifest Manifest(InstallScope scope) => new()
    {
        Name = "ElevationWiring",
        Manufacturer = "Tests",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(), // fresh per manifest so the per-bundle instance lock never collides
        UpgradeCode = Guid.NewGuid(),
        Scope = scope,
        Packages = []
    };

    private string WriteManifest(InstallScope scope)
    {
        var path = Path.Combine(_tempDir, $"manifest_{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            JsonSerializer.Serialize(Manifest(scope), LayoutJsonContext.Default.InstallerManifest));
        return path;
    }

    private EngineSessionOptions Options(bool processElevated) => new()
    {
        LogPath = Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.log"),
        ElevationProbe = new FixedElevationProbe(processElevated)
        // WriteJournal left at its default (true): InstallerPipelineBuilder.Build() only constructs
        // ApplyStep/PackageExecutor when a journal store is present, and the MsiExecutor under test
        // lives inside that PackageExecutor.
    };

    /// <summary>
    /// An MSI path that does not exist, under this test's own temp directory. The in-process path
    /// hands it to Windows Installer, which opens nothing and installs nothing; the point of using
    /// it is that reaching Windows Installer at all is the observable difference from reaching the
    /// gateway.
    /// </summary>
    private string MissingMsiPath() => Path.Combine(_tempDir, $"absent_{Guid.NewGuid():N}.msi");

    /// <summary>
    /// The exit codes <c>MsiInstallProductW</c> can return for a package path that does not exist.
    /// 2 is ERROR_FILE_NOT_FOUND, observed on this development machine. 1619 is
    /// ERROR_INSTALL_PACKAGE_OPEN_FAILED, the code Windows Installer documents for this case and
    /// what a CI runner could plausibly return instead. The test only cares that Windows Installer
    /// was reached at all, not which of the two codes it picked.
    /// </summary>
    private static readonly int[] MissingPackageExitCodes = [2, 1619];

    // Named MsiInstallAction, not InstallAction: FalkForge.Engine.Pipeline already declares an
    // InstallAction enum, and a member with the same name would make every use of the type
    // ambiguous inside this class.
    private PlanAction MsiInstallAction() => new()
    {
        PackageId = "TestMsi",
        ActionType = PlanActionType.Install,
        Package = new PackageInfo
        {
            Id = "TestMsi",
            Type = PackageType.MsiPackage,
            DisplayName = "Test MSI Package",
            SourcePath = MissingMsiPath(),
            Sha256Hash = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            Properties = new Dictionary<string, string>()
        },
        Properties = new Dictionary<string, string>()
    };

    private static object GetField(object target, string fieldName)
    {
        var type = target.GetType();
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        var value = field?.GetValue(target);
        return value ?? throw new InvalidOperationException(
            $"Field '{fieldName}' on '{type.FullName}' was null or missing — production wiring changed shape.");
    }

    /// <summary>
    /// Reaches the shared <see cref="PipelineContext"/> and the very
    /// <see cref="MsiExecutor"/> instance the production wiring built. Both are private, and both
    /// have to be the production instances for these tests to mean anything.
    /// </summary>
    private static (PipelineContext Ctx, MsiExecutor Executor) Probe(EngineSession session)
    {
        var pipeline = GetField(session, "_pipeline");
        var ctx = (PipelineContext)GetField(pipeline, "_ctx");
        var applyStep = GetField(pipeline, "_applyStep");
        var packageExecutor = GetField(applyStep, "_executor");
        var msiExecutor = (MsiExecutor)GetField(packageExecutor, "_msiExecutor");
        return (ctx, msiExecutor);
    }

    /// <summary>
    /// Runs the real <see cref="ElevateStep"/> against the session's own context. The manifest is
    /// passed explicitly: ElevateStep resolves scope as <c>_manifest?.Scope ?? ctx.Manifest?.Scope</c>
    /// and does NOT return early when both are null, so a step built with neither would start the
    /// companion for a per-user bundle.
    /// </summary>
    private static async Task RunElevateStepAsync(
        PipelineContext ctx, IElevatedCommandGateway gateway, IUiChannel channel, InstallScope scope)
    {
        var step = new ElevateStep(gateway, channel, Guid.Empty, Manifest(scope));
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public async Task PerMachine_UnelevatedEngine_SendsTheInstallToTheCompanion()
    {
        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(InstallScope.PerMachine), Options(processElevated: false));
        var (ctx, executor) = Probe(session);
        await using var channel = new FakeUiChannel();
        var gateway = new RecordingGateway();

        await RunElevateStepAsync(ctx, gateway, channel, InstallScope.PerMachine);
        Assert.True(gateway.StartCalled);
        Assert.NotNull(ctx.ElevationGateway);

        var result = await executor.ExecuteAsync(
            MsiInstallAction(), CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, gateway.SendCount);
        Assert.Equal("MsiInstall", gateway.LastCommandName);
    }

    [Fact]
    public async Task PerUser_InstallsInProcessAndNeverContactsTheCompanion()
    {
        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(InstallScope.PerUser), Options(processElevated: false));
        var (ctx, executor) = Probe(session);
        await using var channel = new FakeUiChannel();
        var gateway = new RecordingGateway();

        await RunElevateStepAsync(ctx, gateway, channel, InstallScope.PerUser);
        Assert.False(gateway.StartCalled);
        Assert.Null(ctx.ElevationGateway);

        var result = await executor.ExecuteAsync(
            MsiInstallAction(), CancellationToken.None, new Progress<int>(_ => { }));

        // The gateway is the assertion that matters: nothing crossed to the companion.
        Assert.Equal(0, gateway.SendCount);
        Assert.Null(gateway.LastCommandName);
        // Windows Installer answered instead. The package path does not exist, so it opened
        // nothing and installed nothing. A specific code, not just nonzero, is what tells a future
        // reader that Windows Installer was actually reached. Observed on this host:
        // MsiInstallProductW returns 2 (ERROR_FILE_NOT_FOUND). A CI runner could plausibly see 1619
        // (ERROR_INSTALL_PACKAGE_OPEN_FAILED) instead for the same missing path, so both codes pass.
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(result.Value, MissingPackageExitCodes);
    }

    [Fact]
    public async Task PerMachine_AlreadyElevatedEngine_InstallsInProcessAndNeverContactsTheCompanion()
    {
        // A user who right-clicks the bundle and picks "Run as administrator" gets an elevated
        // engine. That run installs per-machine successfully today, in-process, because the engine
        // already holds the privileges Windows wants. Routing it to the companion would refuse it
        // with INT009 on every build that has no baked publisher key, which is every shipped build.
        // Keeping it in-process is a compatibility decision, not a claim that the in-process path
        // checks as much as the companion does: it skips manifest-envelope verification,
        // install-time hash binding, package-id refusal, TRANSFORMS/PATCH refusal, and UNC refusal.
        // This branch does not widen that gap, it narrows who lands in it.
        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(InstallScope.PerMachine), Options(processElevated: true));
        var (ctx, executor) = Probe(session);
        await using var channel = new FakeUiChannel();
        var gateway = new RecordingGateway();

        // The companion still starts. Per-machine dependency registration and the verified-apply
        // trust-store advance both read ctx.ElevationGateway (ApplyStep.cs:459 and :238), and both
        // work today for an elevated engine. Only the MSI install stays in-process.
        await RunElevateStepAsync(ctx, gateway, channel, InstallScope.PerMachine);
        Assert.True(gateway.StartCalled);
        Assert.NotNull(ctx.ElevationGateway);

        var result = await executor.ExecuteAsync(
            MsiInstallAction(), CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(0, gateway.SendCount);
        Assert.Null(gateway.LastCommandName);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        // Observed on this host: MsiInstallProductW returns 2 (ERROR_FILE_NOT_FOUND) for the
        // missing package path; see the note on PerUser_InstallsInProcessAndNeverContactsTheCompanion
        // for why a CI runner's 1619 also passes.
        Assert.Contains(result.Value, MissingPackageExitCodes);
    }

    [Fact]
    public async Task BeforeElevationRuns_TheInstallDoesNotReachTheCompanion()
    {
        // Ordering guard: nothing has run yet, so the context field is unset and the MSI executor
        // must take the in-process path rather than send to a companion that was never started.
        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(InstallScope.PerMachine), Options(processElevated: false));
        var (ctx, executor) = Probe(session);

        Assert.Null(ctx.ElevationGateway);

        var result = await executor.ExecuteAsync(
            MsiInstallAction(), CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        // Observed on this host: MsiInstallProductW returns 2 (ERROR_FILE_NOT_FOUND) for the missing
        // package path; see the note on PerUser_InstallsInProcessAndNeverContactsTheCompanion for
        // why a CI runner's 1619 also passes.
        Assert.Contains(result.Value, MissingPackageExitCodes);
    }

    [Fact]
    public async Task WithNoProbeSupplied_TheWiringUsesTheRealProcessTokenProbe()
    {
        // The four tests above all inject a probe, so none of them reaches
        // `options.ElevationProbe ?? new DefaultElevationProbe()`. This one supplies no probe and
        // asserts the routing matches what the real probe says, by calling the same internal method
        // DefaultElevationProbe wraps (FalkForge.Engine.csproj:20 grants InternalsVisibleTo here).
        //
        // Be honest about what this catches. It catches a fallback that was deleted, that resolves
        // to null, or that hardcodes the OPPOSITE of the running host's elevation state. On the
        // normal unelevated test host it does NOT catch a fallback that hardcodes false, because
        // false is also the right answer there. Task 8 on a real machine is the only coverage of
        // the probe's own correctness.
        var options = new EngineSessionOptions
        {
            LogPath = Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.log")
            // ElevationProbe deliberately not set.
        };
        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(InstallScope.PerMachine), options);
        var (ctx, executor) = Probe(session);
        await using var channel = new FakeUiChannel();
        var gateway = new RecordingGateway();

        await RunElevateStepAsync(ctx, gateway, channel, InstallScope.PerMachine);

        var result = await executor.ExecuteAsync(
            MsiInstallAction(), CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var hostIsElevated = FalkForge.Engine.Bootstrap.ElevationProbe.IsElevated();
        Assert.Equal(hostIsElevated ? 0 : 1, gateway.SendCount);
    }

    private sealed class FixedElevationProbe : IElevationProbe
    {
        private readonly bool _elevated;
        public FixedElevationProbe(bool elevated) => _elevated = elevated;
        public bool IsElevated() => _elevated;
    }

    private sealed class RecordingGateway : IElevatedCommandGateway
    {
        public bool StartCalled { get; private set; }
        public int SendCount { get; private set; }
        public string? LastCommandName { get; private set; }

        public Task<Result<Unit>> StartAsync(CancellationToken ct)
        {
            StartCalled = true;
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public void SetCorrelationId(Guid id) { }

        public Task<Result<byte[]>> SendCommandAsync(
            string commandName, byte[] payload, IProgress<int>? progress, CancellationToken ct)
        {
            SendCount++;
            LastCommandName = commandName;
            // Four little-endian zero bytes: the shape MsiInstallCommand answers a success with.
            return Task.FromResult(Result<byte[]>.Success(new byte[] { 0, 0, 0, 0 }));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
