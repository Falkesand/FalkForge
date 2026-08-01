namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Dependencies;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Logging;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Platform.Dependencies;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// THE TEST THAT MATTERS for dependency enforcement. Every unit test on the detector, registrar, payload,
/// and elevated command dies to the "wired but the result is discarded" mutant — none of them prove the
/// pieces are actually WIRED TOGETHER into a live pipeline. This file drives three REAL pipeline runs
/// (install / install / plan-uninstall) against ONE shared <see cref="MockRegistry"/>, exactly like three
/// separate installer processes would share one real machine registry, and proves the refusal actually
/// reaches the caller.
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class DependencyLifecycleEndToEndTests
{
    private static PackageInfo ExePackage(string id) =>
        new()
        {
            Id = id,
            Type = PackageType.ExePackage,
            DisplayName = $"Test {id}",
            SourcePath = $@"C:\fake\{id}.exe",
            Sha256Hash = "DEADBEEF",
            Properties = new Dictionary<string, string>
            {
                ["InstallArguments"] = "/quiet /norestart",
                ["UninstallArguments"] = "/quiet /norestart"
            }
        };

    private static InstallerManifest ProviderManifest(string providerKey, InstallScope scope = InstallScope.PerUser) =>
        new()
        {
            Name = "ProviderBundle",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = scope,
            Packages = [ExePackage("ProviderPkg")],
            DependencyProviders = [new ManifestDependencyProvider(providerKey, "1.0.0", "Provider " + providerKey)]
        };

    private static InstallerManifest ConsumerManifest(
        string providerKey, string consumerKey, InstallScope scope = InstallScope.PerUser) =>
        new()
        {
            Name = "ConsumerBundle-" + consumerKey,
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = scope,
            Packages = [ExePackage("ConsumerPkg-" + consumerKey)],
            DependencyConsumers = [new ManifestDependencyConsumer(providerKey, consumerKey)]
        };

    /// <summary>
    /// Simulates the elevated companion's <c>DependencyRegistrationCommand</c> for PerMachine tests
    /// without a cross-project reference to <c>FalkForge.Engine.Elevation</c> (a Windows-only project;
    /// this test project targets plain <c>net10.0</c>). Decodes the wire payload with the SAME
    /// <see cref="DependencyRegistrationPayload"/> codec <see cref="ApplyStep"/> serializes with, and
    /// applies it through the SAME <see cref="DependencyRegistrar"/> the real command uses — only the
    /// out-of-process elevation boundary itself is a test double, exactly like every other elevated-command
    /// test in this codebase (see <see cref="InProcessElevationGateway"/>). The command's own
    /// codec/validation/allowlist behavior is separately unit-tested in
    /// <c>DependencyRegistrationCommandTests</c>.
    /// </summary>
    private static InProcessElevationGateway PerMachineDependencyGateway(MockRegistry registry) =>
        new((commandName, payload, _, _) =>
        {
            if (commandName != "DependencyRegistration")
                return Task.FromResult(Result<byte[]>.Success(Array.Empty<byte>()));

            if (!DependencyRegistrationPayload.TryDeserialize(
                    payload, out var opcode, out var bundleId, out var providers, out var consumers))
            {
                return Task.FromResult(Result<byte[]>.Failure(
                    ErrorKind.SecurityError, "malformed DependencyRegistration payload"));
            }

            var registrar = new DependencyRegistrar(registry);
            if (opcode == DependencyRegistrationOpcode.Register)
            {
                foreach (var provider in providers)
                    registrar.RegisterProvider(RegistryRoot.LocalMachine, provider.Key, provider.Version, provider.DisplayName);

                foreach (var consumer in consumers)
                    registrar.RegisterConsumer(RegistryRoot.LocalMachine, consumer.ProviderKey, consumer.ConsumerKey, bundleId);
            }
            else
            {
                foreach (var consumer in consumers)
                    registrar.UnregisterConsumer(RegistryRoot.LocalMachine, consumer.ProviderKey, consumer.ConsumerKey);
            }

            return Task.FromResult(Result<byte[]>.Success(Array.Empty<byte>()));
        });

    private static InstallerManifest RequirementManifest(string requiredProviderKey) =>
        new()
        {
            Name = "RequirerBundle",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [ExePackage("RequirerPkg")],
            DependencyRequirements = [new ManifestDependencyRequirement(requiredProviderKey, "1.0.0", null, true, false)]
        };

    private static UiRequest.Plan RequestFor(InstallAction action) =>
        new(action, InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>());

    private static PackageExecutor SucceedingExecutor()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        return new PackageExecutor(
            new MsiExecutor(), new MsuExecutor(runner), new MspExecutor(runner),
            new BundleExecutor(runner), new ExeExecutor(runner), new NetRuntimeExecutor(runner));
    }

    /// <summary>
    /// Drives one full, real pipeline lifecycle (Detect → Plan → Elevate → Apply) for
    /// <paramref name="manifest"/> against the SHARED <paramref name="registry"/> — modelling one
    /// installer process's run against the machine's real registry. Short-circuits with the first
    /// failing phase's Result, exactly like <see cref="PipelineRunner"/> does.
    /// </summary>
    private static async Task<Result<Unit>> RunLifecycleAsync(
        InstallerManifest manifest, InstallAction action, MockRegistry registry, bool ignoreDependencies = false,
        InProcessElevationGateway? elevationGateway = null)
    {
        var builder = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(registry)
            .WithPackageExecutor(SucceedingExecutor())
            .WithJournalStore(new InMemoryJournalStore())
            .WithUiChannel(new FakeUiChannel());
        if (ignoreDependencies)
            builder = builder.WithIgnoreDependencies();
        if (elevationGateway is not null)
            builder = builder.WithElevationGateway(elevationGateway);

        await using var pipeline = builder.Build();

        var detect = await pipeline.DetectAsync(CancellationToken.None);
        if (detect.IsFailure)
            return Result<Unit>.Failure(detect.Error);

        var plan = await pipeline.PlanAsync(RequestFor(action), CancellationToken.None);
        if (plan.IsFailure)
            return Result<Unit>.Failure(plan.Error);

        var elevate = await pipeline.ElevateAsync(CancellationToken.None);
        if (elevate.IsFailure)
            return Result<Unit>.Failure(elevate.Error);

        return await pipeline.ApplyAsync(CancellationToken.None);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // The core proof: shared provider blocks uninstall until its consumer is gone
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProviderInstalled_ThenConsumerInstalled_UninstallOfProvider_IsRefused_NamesBothKeys()
    {
        var registry = new MockRegistry();

        var installProvider = await RunLifecycleAsync(ProviderManifest("SharedLib"), InstallAction.Install, registry);
        Assert.True(installProvider.IsSuccess, installProvider.IsFailure ? installProvider.Error.Message : null);

        var installConsumer = await RunLifecycleAsync(
            ConsumerManifest("SharedLib", "ConsumerApp"), InstallAction.Install, registry);
        Assert.True(installConsumer.IsSuccess, installConsumer.IsFailure ? installConsumer.Error.Message : null);

        var uninstallProvider = await RunLifecycleAsync(ProviderManifest("SharedLib"), InstallAction.Uninstall, registry);

        Assert.True(uninstallProvider.IsFailure);
        Assert.Equal(ErrorKind.PlanningError, uninstallProvider.Error.Kind);
        Assert.Contains("SharedLib", uninstallProvider.Error.Message);
        Assert.Contains("ConsumerApp", uninstallProvider.Error.Message);
    }

    [Fact]
    public async Task UninstallingConsumerFirst_ThenProvider_Succeeds()
    {
        // Kills "registration written but never removed" — if unregister didn't actually happen, the
        // provider would stay refused forever.
        var registry = new MockRegistry();
        await RunLifecycleAsync(ProviderManifest("SharedLib"), InstallAction.Install, registry);
        await RunLifecycleAsync(ConsumerManifest("SharedLib", "ConsumerApp"), InstallAction.Install, registry);

        var uninstallConsumer = await RunLifecycleAsync(
            ConsumerManifest("SharedLib", "ConsumerApp"), InstallAction.Uninstall, registry);
        Assert.True(uninstallConsumer.IsSuccess, uninstallConsumer.IsFailure ? uninstallConsumer.Error.Message : null);

        var uninstallProvider = await RunLifecycleAsync(ProviderManifest("SharedLib"), InstallAction.Uninstall, registry);
        Assert.True(uninstallProvider.IsSuccess, uninstallProvider.IsFailure ? uninstallProvider.Error.Message : null);
    }

    [Fact]
    public async Task TwoConsumers_RemovingOne_StillRefusesProviderUninstall()
    {
        // Reference-count proof end to end: the provider must survive as long as ANY consumer remains.
        var registry = new MockRegistry();
        await RunLifecycleAsync(ProviderManifest("SharedLib"), InstallAction.Install, registry);
        await RunLifecycleAsync(ConsumerManifest("SharedLib", "ConsumerA"), InstallAction.Install, registry);
        await RunLifecycleAsync(ConsumerManifest("SharedLib", "ConsumerB"), InstallAction.Install, registry);

        var uninstallConsumerA = await RunLifecycleAsync(
            ConsumerManifest("SharedLib", "ConsumerA"), InstallAction.Uninstall, registry);
        Assert.True(uninstallConsumerA.IsSuccess);

        var uninstallProvider = await RunLifecycleAsync(ProviderManifest("SharedLib"), InstallAction.Uninstall, registry);

        Assert.True(uninstallProvider.IsFailure);
        Assert.Equal(ErrorKind.PlanningError, uninstallProvider.Error.Kind);
        Assert.Contains("ConsumerB", uninstallProvider.Error.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PerMachine: the only prior PerMachine coverage anywhere in the suite checked that the elevated
    // command NAME was sent (ApplyStepDependencyRegistrationTests) — nothing exercised a full PerMachine
    // lifecycle through Detect -> Plan -> Elevate -> Apply against a shared HKLM-rooted registry, the
    // same shape the PerUser tests above already prove.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PerMachine_ProviderInstalled_ThenConsumerInstalled_UninstallOfProvider_IsRefused()
    {
        var registry = new MockRegistry();
        var gateway = PerMachineDependencyGateway(registry);

        var installProvider = await RunLifecycleAsync(
            ProviderManifest("SharedLib", InstallScope.PerMachine), InstallAction.Install, registry,
            elevationGateway: gateway);
        Assert.True(installProvider.IsSuccess, installProvider.IsFailure ? installProvider.Error.Message : null);

        var installConsumer = await RunLifecycleAsync(
            ConsumerManifest("SharedLib", "ConsumerApp", InstallScope.PerMachine), InstallAction.Install, registry,
            elevationGateway: gateway);
        Assert.True(installConsumer.IsSuccess, installConsumer.IsFailure ? installConsumer.Error.Message : null);

        var uninstallProvider = await RunLifecycleAsync(
            ProviderManifest("SharedLib", InstallScope.PerMachine), InstallAction.Uninstall, registry,
            elevationGateway: gateway);

        Assert.True(uninstallProvider.IsFailure);
        Assert.Equal(ErrorKind.PlanningError, uninstallProvider.Error.Kind);
        Assert.Contains("SharedLib", uninstallProvider.Error.Message);
        Assert.Contains("ConsumerApp", uninstallProvider.Error.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Install-side: missing provider blocks install; installing it unblocks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Install_RequiredProviderMissing_IsRefused()
    {
        var registry = new MockRegistry();

        var install = await RunLifecycleAsync(RequirementManifest("SharedLib"), InstallAction.Install, registry);

        Assert.True(install.IsFailure);
        Assert.Equal(ErrorKind.PlanningError, install.Error.Kind);
        Assert.Contains("SharedLib", install.Error.Message);
    }

    [Fact]
    public async Task Install_RequiredProviderInstalledFirst_Succeeds()
    {
        var registry = new MockRegistry();
        await RunLifecycleAsync(ProviderManifest("SharedLib"), InstallAction.Install, registry);

        var install = await RunLifecycleAsync(RequirementManifest("SharedLib"), InstallAction.Install, registry);

        Assert.True(install.IsSuccess, install.IsFailure ? install.Error.Message : null);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // --ignore-dependencies escape hatch
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IgnoreDependencies_UninstallOfBlockedProvider_Proceeds()
    {
        var registry = new MockRegistry();
        await RunLifecycleAsync(ProviderManifest("SharedLib"), InstallAction.Install, registry);
        await RunLifecycleAsync(ConsumerManifest("SharedLib", "ConsumerApp"), InstallAction.Install, registry);

        var uninstallProvider = await RunLifecycleAsync(
            ProviderManifest("SharedLib"), InstallAction.Uninstall, registry, ignoreDependencies: true);

        Assert.True(uninstallProvider.IsSuccess, uninstallProvider.IsFailure ? uninstallProvider.Error.Message : null);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PipelineRunner-level: the refusal must reach the user, never be swallowed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PipelineRunner_RefusedUninstall_EmitsFailedEvent_AndReturnsExitCode1()
    {
        var registry = new MockRegistry();
        await RunLifecycleAsync(ProviderManifest("SharedLib"), InstallAction.Install, registry);
        await RunLifecycleAsync(ConsumerManifest("SharedLib", "ConsumerApp"), InstallAction.Install, registry);

        var channel = new FakeUiChannel();
        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(ProviderManifest("SharedLib"))
            .WithRegistry(registry)
            .WithPackageExecutor(SucceedingExecutor())
            .WithJournalStore(new InMemoryJournalStore())
            .WithUiChannel(channel)
            .Build();
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(RequestFor(InstallAction.Uninstall));
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains(channel.SentEvents, e => e is PipelineEvent.Failed);
        var failed = Assert.Single(channel.SentEvents.OfType<PipelineEvent.Failed>());
        Assert.Equal(ErrorKind.PlanningError, failed.Kind);
    }
}
