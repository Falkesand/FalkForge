namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Step 7: <see cref="PlanStep"/>'s dependency-enforcement gate, inserted between the architecture gate
/// and the license gate — machine-state refusals before the consent gate. Uninstall fails closed on a
/// registry read error; install fails when a required provider is missing/unsatisfied. Both are
/// bypassable with <c>--ignore-dependencies</c> (<see cref="PipelineContext.IgnoreDependencies"/>), except
/// silent mode must NOT auto-imply that override (silent uninstall is automation — exactly where silent
/// breakage hurts most).
/// </summary>
public sealed class DependencyGatePlanStepTests
{
    private static UiRequest.Plan RequestFor(InstallAction action) =>
        new(action, InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>());

    private static PackageInfo Package(string id = "Pkg1") =>
        new()
        {
            Id = id,
            Type = PackageType.ExePackage,
            DisplayName = $"Test {id}",
            SourcePath = $@"C:\fake\{id}.exe",
            Sha256Hash = "DEADBEEF"
        };

    private static InstallerManifest ManifestWith(
        ManifestDependencyProvider[]? providers = null,
        ManifestDependencyConsumer[]? consumers = null,
        ManifestDependencyRequirement[]? requirements = null) =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [Package()],
            DependencyProviders = providers ?? [],
            DependencyConsumers = consumers ?? [],
            DependencyRequirements = requirements ?? []
        };

    private static PipelineContext CtxWith(InstallerManifest manifest, bool silentMode = false) =>
        new()
        {
            Manifest = manifest,
            Detection = new DetectionResult(InstallState.NotInstalled, null, []),
            SilentMode = silentMode
        };

    // ──────────────────────────────────────────────────────────────────────────
    // Uninstall gate — fail closed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Uninstall_BlockedByDependent_ReturnsPlanningError_NamesProviderAndDependent()
    {
        var registry = new MockRegistry();
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents");
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents\OtherApp");

        var manifest = ManifestWith(providers: [new ManifestDependencyProvider("SharedLib", "1.0.0", null)]);
        var ctx = CtxWith(manifest);
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel, registry: registry);

        var result = await step.ExecuteAsync(ctx, RequestFor(InstallAction.Uninstall), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PlanningError, result.Error.Kind);
        Assert.Contains("SharedLib", result.Error.Message);
        Assert.Contains("OtherApp", result.Error.Message);
    }

    [Fact]
    public async Task Uninstall_NoDependents_Succeeds()
    {
        var registry = new MockRegistry();
        var manifest = ManifestWith(providers: [new ManifestDependencyProvider("SharedLib", "1.0.0", null)]);
        var ctx = CtxWith(manifest);
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel, registry: registry);

        var result = await step.ExecuteAsync(ctx, RequestFor(InstallAction.Uninstall), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public async Task Uninstall_RegistryReadFails_FailsClosed_NeverTreatsUnknownAsNone()
    {
        var registry = new MockRegistry();
        registry.FailReadsUnder(@"SOFTWARE\Classes\Installer\Dependencies");

        var manifest = ManifestWith(providers: [new ManifestDependencyProvider("SharedLib", "1.0.0", null)]);
        var ctx = CtxWith(manifest);
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel, registry: registry);

        var result = await step.ExecuteAsync(ctx, RequestFor(InstallAction.Uninstall), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PlanningError, result.Error.Kind);
    }

    [Fact]
    public async Task Uninstall_IgnoreDependencies_ProceedsDespiteBlocker()
    {
        var registry = new MockRegistry();
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents");
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents\OtherApp");

        var manifest = ManifestWith(providers: [new ManifestDependencyProvider("SharedLib", "1.0.0", null)]);
        var ctx = CtxWith(manifest);
        ctx.IgnoreDependencies = true;
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel, registry: registry);

        var result = await step.ExecuteAsync(ctx, RequestFor(InstallAction.Uninstall), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public async Task Uninstall_SilentMode_DoesNotAutoImplyIgnoreDependencies()
    {
        // Silent uninstall is automation — exactly where silent breakage hurts most. Silent mode must
        // NOT bypass the dependency gate on its own; only the explicit override does.
        var registry = new MockRegistry();
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents");
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents\OtherApp");

        var manifest = ManifestWith(providers: [new ManifestDependencyProvider("SharedLib", "1.0.0", null)]);
        var ctx = CtxWith(manifest, silentMode: true);
        // IgnoreDependencies intentionally left false.
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel, registry: registry);

        var result = await step.ExecuteAsync(ctx, RequestFor(InstallAction.Uninstall), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PlanningError, result.Error.Kind);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Install gate
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Install_RequiredProviderMissing_ReturnsPlanningError()
    {
        var registry = new MockRegistry();
        var manifest = ManifestWith(
            requirements: [new ManifestDependencyRequirement("SharedLib", "1.0.0", null, true, false)]);
        var ctx = CtxWith(manifest);
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel, registry: registry);

        var result = await step.ExecuteAsync(ctx, RequestFor(InstallAction.Install), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PlanningError, result.Error.Kind);
        Assert.Contains("SharedLib", result.Error.Message);
    }

    [Fact]
    public async Task Install_RequiredProviderSatisfied_Succeeds()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib", "Version", "2.0.0");

        var manifest = ManifestWith(
            requirements: [new ManifestDependencyRequirement("SharedLib", "1.0.0", null, true, false)]);
        var ctx = CtxWith(manifest);
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel, registry: registry);

        var result = await step.ExecuteAsync(ctx, RequestFor(InstallAction.Install), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public async Task Install_RegistryReadFails_FailsClosed_NeverTreatsUnreadableAsMissing()
    {
        // Mirrors Uninstall_RegistryReadFails_FailsClosed: an unreadable registry must refuse the
        // install with a distinct "could not verify" message, never a silent permit and never a
        // misleading "provider not satisfied" message naming a provider that may well be present.
        var registry = new MockRegistry();
        registry.FailReadsUnder(@"SOFTWARE\Classes\Installer\Dependencies");

        var manifest = ManifestWith(
            requirements: [new ManifestDependencyRequirement("SharedLib", "1.0.0", null, true, false)]);
        var ctx = CtxWith(manifest);
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel, registry: registry);

        var result = await step.ExecuteAsync(ctx, RequestFor(InstallAction.Install), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PlanningError, result.Error.Kind);
        Assert.Contains("Cannot verify dependency state safely", result.Error.Message);
        Assert.DoesNotContain("not satisfied", result.Error.Message);
    }

    [Fact]
    public async Task Install_IgnoreDependencies_ProceedsDespiteMissingProvider()
    {
        var registry = new MockRegistry();
        var manifest = ManifestWith(
            requirements: [new ManifestDependencyRequirement("SharedLib", "1.0.0", null, true, false)]);
        var ctx = CtxWith(manifest);
        ctx.IgnoreDependencies = true;
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel, registry: registry);

        var result = await step.ExecuteAsync(ctx, RequestFor(InstallAction.Install), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Backward compatibility — no registry injected skips the gate entirely
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoRegistryInjected_SkipsGateEntirely_UninstallSucceedsEvenWithBlocker()
    {
        var manifest = ManifestWith(providers: [new ManifestDependencyProvider("SharedLib", "1.0.0", null)]);
        var ctx = CtxWith(manifest);
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel); // no registry passed

        var result = await step.ExecuteAsync(ctx, RequestFor(InstallAction.Uninstall), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
