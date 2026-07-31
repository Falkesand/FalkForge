namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Pins the <c>Privileged</c> built-in against the architecture this engine actually runs under:
/// <c>FalkForge.Engine</c> is <c>asInvoker</c> (see <c>app.manifest</c>) and performs per-machine
/// work through a separate elevated companion process (<see cref="ElevateStep"/>), reached at
/// <see cref="EnginePhase.Elevating"/> — TWO PHASES AFTER <see cref="DetectStep"/> seeds
/// this variable. Seeding <c>Privileged</c> from the CURRENT process token's elevation state alone
/// (the third probe this variable has had — a registry key readable by any user, then
/// <c>IEnvironment.IsElevated</c>) is answering the wrong question for this architecture: on the
/// normal double-click flow the engine process itself is never elevated even when it is perfectly
/// able to perform a per-machine install via the companion, so a package gated on
/// <c>Condition.IsPrivileged</c> would be silently skipped every time.
/// <para>
/// <c>Privileged</c> must answer "can THIS INSTALL perform privileged work", which is: the process
/// token is already elevated (covers <c>BindToChannel</c>/headless/test hosts that run elevated
/// directly with no companion), OR an elevation companion is configured and available (covers the
/// normal asInvoker + companion architecture). Both inputs are drilled through the real
/// <see cref="InstallerPipelineBuilder"/> → <see cref="IInstallerPipeline"/> → <see cref="DetectStep"/>
/// path (not a unit test on <c>BuiltInVariables.Populate</c> in isolation) because the wiring
/// between them — <see cref="InstallerPipelineBuilder.WithElevationCompanionAvailable"/> reaching
/// <see cref="DetectStep"/> reaching <c>BuiltInVariables.Populate</c> — is exactly the kind of
/// connection this variable's history shows gets silently dropped.
/// </para>
/// </summary>
public sealed class BuiltInVariablePrivilegedWiringTests
{
    private static InstallerManifest EmptyManifest() =>
        new()
        {
            Name = "PrivilegedWiring",
            Manufacturer = "Tests",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = []
        };

    private static async Task<VariableStore> DetectAndReturnStoreAsync(
        bool processElevated, bool elevationCompanionAvailable)
    {
        var registry = new MockRegistry();
        var store = new VariableStore();
        var environment = new FakeEnvironment { IsElevated = processElevated };
        await using var channel = new FakeUiChannel();

        var builder = new InstallerPipelineBuilder()
            .WithManifest(EmptyManifest())
            .WithRegistry(registry)
            .WithVariableStore(store)
            .WithPlatformServices(new FakePlatformServices(registry, environment))
            .WithUiChannel(channel);

        if (elevationCompanionAvailable)
            builder = builder.WithElevationCompanionAvailable();

        await using var pipeline = builder.Build();

        var detectResult = await pipeline.DetectAsync(CancellationToken.None);
        Assert.True(detectResult.IsSuccess);

        return store;
    }

    [Fact]
    public async Task ElevatedProcess_NoCompanion_PrivilegedIsOne()
    {
        var store = await DetectAndReturnStoreAsync(processElevated: true, elevationCompanionAvailable: false);

        var privileged = store.TryGet<long>(BuiltInVariableNames.Privileged);
        Assert.True(privileged.IsSuccess);
        Assert.Equal(1L, privileged.Value);
    }

    [Fact]
    public async Task UnelevatedProcess_WithCompanionConfigured_PrivilegedIsOne()
    {
        // The normal asInvoker + elevation-companion architecture: the engine process itself is
        // never elevated, but the install CAN still do per-machine work via the companion.
        var store = await DetectAndReturnStoreAsync(processElevated: false, elevationCompanionAvailable: true);

        var privileged = store.TryGet<long>(BuiltInVariableNames.Privileged);
        Assert.True(privileged.IsSuccess);
        Assert.Equal(1L, privileged.Value);
    }

    [Fact]
    public async Task UnelevatedProcess_NoCompanion_PrivilegedIsZero()
    {
        var store = await DetectAndReturnStoreAsync(processElevated: false, elevationCompanionAvailable: false);

        var privileged = store.TryGet<long>(BuiltInVariableNames.Privileged);
        Assert.True(privileged.IsSuccess);
        Assert.Equal(0L, privileged.Value);
    }
}
