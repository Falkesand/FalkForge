namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Platform.Dependencies;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Step 6: <see cref="ApplyStep"/> is the write-side wiring for runtime dependency enforcement — it
/// registers providers/consumers after a successful install and unregisters this bundle's own consumer
/// entries after a successful uninstall. This is what makes the detector (<c>DependencyDetector</c>,
/// wired into <c>PlanStep</c>) see anything real instead of an always-empty registry.
/// </summary>
public sealed class ApplyStepDependencyRegistrationTests
{
    private static readonly Guid BundleId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static PackageInfo ExePackage(string id = "Pkg1") =>
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

    private static InstallerManifest ManifestWithDependencies(
        InstallScope scope, PackageInfo package) =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = BundleId,
            UpgradeCode = Guid.NewGuid(),
            Scope = scope,
            Packages = [package],
            DependencyProviders = [new ManifestDependencyProvider("SharedLib", "1.0.0", "Shared Library")],
            DependencyConsumers = [new ManifestDependencyConsumer("SharedLib", "AppA")]
        };

    private static InstallPlan PlanFor(PackageInfo pkg, PlanActionType actionType) =>
        new()
        {
            Actions =
            [
                new PlanAction { PackageId = pkg.Id, ActionType = actionType, Package = pkg }
            ]
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

    private static PackageExecutor FailingExecutor()
    {
        var runner = new MockProcessRunner().WithExitCode(1602);
        return new PackageExecutor(
            new MsiExecutor(), new MsuExecutor(runner), new MspExecutor(runner),
            new BundleExecutor(runner), new ExeExecutor(runner), new NetRuntimeExecutor(runner));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PerUser — direct write via IRegistry, no elevation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Install_PerUser_RegistersProviderAndConsumerUnderCurrentUser()
    {
        var registry = new MockRegistry();
        var pkg = ExePackage();
        var ctx = new PipelineContext
        {
            Manifest = ManifestWithDependencies(InstallScope.PerUser, pkg),
            Plan = PlanFor(pkg, PlanActionType.Install),
            PlanRequest = RequestFor(InstallAction.Install)
        };

        var step = new ApplyStep(SucceedingExecutor(), new InMemoryJournalStore(), new FakeUiChannel(), registry);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("1.0.0", registry.GetStringValue(
            RegistryRoot.CurrentUser, DependencyRegistrationPaths.ProviderKeyPath("SharedLib"), "Version"));
        Assert.True(registry.KeyExists(
            RegistryRoot.CurrentUser, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
    }

    [Fact]
    public async Task Install_PerUser_UnsafeConsumerKey_RefusesWrite_NeverTouchesRegistry()
    {
        // Commit A regression: before the fix, ApplyStep built DependencyRegistrar directly with the
        // RAW manifest-sourced ConsumerKey — no validation anywhere on this path. A crafted key
        // containing a backslash would nest straight under another product's existing consumer subkey
        // (unexpected subkey structure, per the guard's own doc comment) instead of being rejected. Post-
        // fix, DependencyRegistrar's shared safe-segment guard refuses it before any IRegistry call.
        var registry = new MockRegistry();
        var registrar = new DependencyRegistrar(registry);
        registrar.RegisterProvider(RegistryRoot.CurrentUser, "SharedLib", "1.0.0", "Shared Library");
        registrar.RegisterConsumer(RegistryRoot.CurrentUser, "SharedLib", "LegitApp", Guid.NewGuid());

        var pkg = ExePackage();
        var maliciousManifest = new InstallerManifest
        {
            Name = "EvilApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [pkg],
            DependencyConsumers = [new ManifestDependencyConsumer("SharedLib", @"LegitApp\Injected")]
        };
        var ctx = new PipelineContext
        {
            Manifest = maliciousManifest,
            Plan = PlanFor(pkg, PlanActionType.Install),
            PlanRequest = RequestFor(InstallAction.Install)
        };

        var step = new ApplyStep(SucceedingExecutor(), new InMemoryJournalStore(), new FakeUiChannel(), registry);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        // Best-effort: the apply itself never fails because of a dependency-registration refusal.
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.False(registry.KeyExists(
            RegistryRoot.CurrentUser, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", @"LegitApp\Injected")));
        // The pre-existing legitimate registration must survive untouched.
        Assert.True(registry.KeyExists(
            RegistryRoot.CurrentUser, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "LegitApp")));
    }

    [Fact]
    public async Task Uninstall_PerUser_UnregistersConsumerOnly_LeavesProviderKey()
    {
        var registry = new MockRegistry();
        var registrar = new DependencyRegistrar(registry);
        registrar.RegisterProvider(RegistryRoot.CurrentUser, "SharedLib", "1.0.0", "Shared Library");
        registrar.RegisterConsumer(RegistryRoot.CurrentUser, "SharedLib", "AppA", BundleId);

        var pkg = ExePackage();
        var ctx = new PipelineContext
        {
            Manifest = ManifestWithDependencies(InstallScope.PerUser, pkg),
            Plan = PlanFor(pkg, PlanActionType.Uninstall),
            PlanRequest = RequestFor(InstallAction.Uninstall)
        };

        var step = new ApplyStep(SucceedingExecutor(), new InMemoryJournalStore(), new FakeUiChannel(), registry);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.False(registry.KeyExists(
            RegistryRoot.CurrentUser, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
        Assert.True(registry.KeyExists(
            RegistryRoot.CurrentUser, DependencyRegistrationPaths.ProviderKeyPath("SharedLib")));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Never fails the install; never writes when it shouldn't
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Install_DryRun_WritesNothing()
    {
        var registry = new MockRegistry();
        var pkg = ExePackage();
        var ctx = new PipelineContext
        {
            Manifest = ManifestWithDependencies(InstallScope.PerUser, pkg),
            Plan = PlanFor(pkg, PlanActionType.Install),
            PlanRequest = RequestFor(InstallAction.Install),
            IsDryRun = true
        };

        var step = new ApplyStep(SucceedingExecutor(), new InMemoryJournalStore(), new FakeUiChannel(), registry);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(registry.KeyExists(
            RegistryRoot.CurrentUser, DependencyRegistrationPaths.ProviderKeyPath("SharedLib")));
        Assert.False(registry.KeyExists(
            RegistryRoot.CurrentUser, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
    }

    [Fact]
    public async Task Install_FailedExecution_WritesNothing()
    {
        var registry = new MockRegistry();
        var pkg = ExePackage();
        var ctx = new PipelineContext
        {
            Manifest = ManifestWithDependencies(InstallScope.PerUser, pkg),
            Plan = PlanFor(pkg, PlanActionType.Install),
            PlanRequest = RequestFor(InstallAction.Install)
        };

        var step = new ApplyStep(FailingExecutor(), new InMemoryJournalStore(), new FakeUiChannel(), registry);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(registry.KeyExists(
            RegistryRoot.CurrentUser, DependencyRegistrationPaths.ProviderKeyPath("SharedLib")));
        Assert.False(registry.KeyExists(
            RegistryRoot.CurrentUser, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
    }

    [Fact]
    public async Task NoRegistryInjected_SkipsRegistrationEntirely_StillSucceeds()
    {
        // Backward compatibility: existing callers that never pass a registry must be unaffected.
        var pkg = ExePackage();
        var ctx = new PipelineContext
        {
            Manifest = ManifestWithDependencies(InstallScope.PerUser, pkg),
            Plan = PlanFor(pkg, PlanActionType.Install),
            PlanRequest = RequestFor(InstallAction.Install)
        };

        var step = new ApplyStep(SucceedingExecutor(), new InMemoryJournalStore(), new FakeUiChannel());
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PerMachine — through the elevated companion
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Install_PerMachine_WithElevationGateway_SendsDependencyRegistrationCommand()
    {
        var registry = new MockRegistry();
        var gateway = InProcessElevationGateway.AlwaysSucceeds();
        await gateway.StartAsync(CancellationToken.None);

        var pkg = ExePackage();
        var ctx = new PipelineContext
        {
            Manifest = ManifestWithDependencies(InstallScope.PerMachine, pkg),
            Plan = PlanFor(pkg, PlanActionType.Install),
            PlanRequest = RequestFor(InstallAction.Install),
            ElevationGateway = gateway
        };

        var step = new ApplyStep(SucceedingExecutor(), new InMemoryJournalStore(), new FakeUiChannel(), registry);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(gateway.SentCommands, c => c.CommandName == "DependencyRegistration");
    }

    [Fact]
    public async Task Install_PerMachine_NoElevationGateway_SkipsRegistration_StillSucceeds()
    {
        // A persistence failure (or, here, unavailability) must never fail the install.
        var registry = new MockRegistry();
        var pkg = ExePackage();
        var ctx = new PipelineContext
        {
            Manifest = ManifestWithDependencies(InstallScope.PerMachine, pkg),
            Plan = PlanFor(pkg, PlanActionType.Install),
            PlanRequest = RequestFor(InstallAction.Install)
        };

        var step = new ApplyStep(SucceedingExecutor(), new InMemoryJournalStore(), new FakeUiChannel(), registry);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public async Task Install_PerMachine_ElevatedCommandFails_StillSucceeds()
    {
        // Best-effort: a rejected/failed elevated write is logged, never fails the install.
        var registry = new MockRegistry();
        var gateway = InProcessElevationGateway.AlwaysFails("boom");
        await gateway.StartAsync(CancellationToken.None);

        var pkg = ExePackage();
        var ctx = new PipelineContext
        {
            Manifest = ManifestWithDependencies(InstallScope.PerMachine, pkg),
            Plan = PlanFor(pkg, PlanActionType.Install),
            PlanRequest = RequestFor(InstallAction.Install),
            ElevationGateway = gateway
        };

        var step = new ApplyStep(SucceedingExecutor(), new InMemoryJournalStore(), new FakeUiChannel(), registry);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }
}
