namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Logging;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Testing;
using Xunit;

using MockRegistry = FalkForge.Testing.MockRegistry;

/// <summary>
/// Verifies that the pipeline steps emit the per-package and per-related-bundle
/// lifecycle events, once per package/bundle, in chain order, interleaved with the
/// phase-level events. These are observational Engine → UI notifications.
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class PerPackageLifecycleStepTests
{
    private static PackageInfo ExePackage(string id) =>
        new()
        {
            Id = id,
            Type = PackageType.ExePackage,
            DisplayName = $"Display {id}",
            SourcePath = $@"C:\fake\{id}.exe",
            Sha256Hash = "DEADBEEF",
            Properties = new Dictionary<string, string>
            {
                ["InstallArguments"] = "/quiet",
                ["UninstallArguments"] = "/quiet"
            }
        };

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

    private static UiRequest.Plan InstallRequest() =>
        new(
            InstallAction.Install,
            InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>());

    private static PackageExecutor DryRunExecutor()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        return new PackageExecutor(
            new MsiExecutor(),
            new MsuExecutor(runner),
            new MspExecutor(runner),
            new BundleExecutor(runner),
            new ExeExecutor(runner),
            new NetRuntimeExecutor(runner));
    }

    [Fact]
    public async Task DetectStep_EmitsDetectPackageComplete_PerPackage_InOrder()
    {
        var manifest = ManifestWith(ExePackage("Pkg1"), ExePackage("Pkg2"), ExePackage("Pkg3"));
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var step = new DetectStep(manifest, new MockRegistry(), channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var ids = channel.SentEvents
            .OfType<PipelineEvent.DetectPackageComplete>()
            .Select(e => e.PackageId)
            .ToList();

        Assert.Equal(["Pkg1", "Pkg2", "Pkg3"], ids);
    }

    [Fact]
    public async Task DetectStep_DetectPackageComplete_ReportsNotInstalledState()
    {
        var manifest = ManifestWith(ExePackage("Pkg1"));
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var step = new DetectStep(manifest, new MockRegistry(), channel);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        var evt = channel.SentEvents.OfType<PipelineEvent.DetectPackageComplete>().Single();
        Assert.Equal("Pkg1", evt.PackageId);
        Assert.Equal(InstallState.NotInstalled, evt.State);
    }

    private static InstallerManifest ManifestWithRelatedBundle(string upgradeCode) =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [ExePackage("Pkg1")],
            RelatedBundles = [new RelatedBundleEntry { BundleId = upgradeCode, Relation = RelatedBundleRelation.Upgrade }]
        };

    private static MockRegistry RegistryWithInstalledBundle(string upgradeCode)
    {
        var registry = new MockRegistry();
        registry.SetStringValue(
            RegistryRoot.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OldApp",
            "BundleUpgradeCode",
            upgradeCode);
        registry.SetStringValue(
            RegistryRoot.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OldApp",
            "DisplayVersion",
            "0.9.0");
        return registry;
    }

    [Fact]
    public async Task DetectStep_EmitsDetectRelatedBundle_WithoutPopulatingContext()
    {
        var upgradeCode = "{ABCDEF01-0000-0000-0000-000000000001}";
        var manifest = ManifestWithRelatedBundle(upgradeCode);
        var registry = RegistryWithInstalledBundle(upgradeCode);

        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var step = new DetectStep(manifest, registry, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = channel.SentEvents.OfType<PipelineEvent.DetectRelatedBundle>().Single();
        Assert.Equal(upgradeCode, evt.BundleId);
        Assert.Equal(RelatedBundleRelation.Upgrade, evt.Relation);
        Assert.Equal("0.9.0", evt.InstalledVersion);

        // The event is OBSERVATIONAL: the detected related bundle must NOT be written into the
        // context. Populating it would activate the planner's related-bundle uninstall path, which
        // synthesizes an empty-SourcePath Uninstall action that aborts the apply. Detection here
        // notifies the UI only and must not change what the planner does.
        Assert.Empty(ctx.RelatedBundles);
    }

    [Fact]
    public async Task DetectThenPlan_WithDetectedRelatedBundle_ProducesNoExtraUninstallAction()
    {
        // Regression guard: a manifest declaring an Upgrade-relation related bundle that IS installed
        // on the machine must still plan a single Install action for its own package — the observational
        // related-bundle detection must not inject a related-bundle Uninstall action into the plan.
        var upgradeCode = "{ABCDEF01-0000-0000-0000-000000000002}";
        var manifest = ManifestWithRelatedBundle(upgradeCode);
        var registry = RegistryWithInstalledBundle(upgradeCode);

        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var detect = new DetectStep(manifest, registry, channel);
        Assert.True((await detect.ExecuteAsync(ctx, CancellationToken.None)).IsSuccess);

        var plan = new PlanStep(new Planner(), channel);
        Assert.True((await plan.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None)).IsSuccess);

        Assert.NotNull(ctx.Plan);
        var action = Assert.Single(ctx.Plan!.Actions);
        Assert.Equal("Pkg1", action.PackageId);
        Assert.Equal(PlanActionType.Install, action.ActionType);
    }

    [Fact]
    public async Task DetectStep_PerPackageEvents_ComeAfterPhaseChangedDetecting()
    {
        var manifest = ManifestWith(ExePackage("Pkg1"));
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var step = new DetectStep(manifest, new MockRegistry(), channel);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        var events = channel.SentEvents;
        var phaseIdx = IndexOfFirst(events, e => e is PipelineEvent.PhaseChanged { Phase: EnginePhase.Detecting });
        var pkgIdx = IndexOfFirst(events, e => e is PipelineEvent.DetectPackageComplete);

        Assert.True(phaseIdx >= 0 && pkgIdx > phaseIdx,
            "DetectPackageComplete must be emitted after PhaseChanged(Detecting)");
    }

    [Fact]
    public async Task PlanStep_EmitsPlanPackageBeginThenComplete_PerPackage_InOrder()
    {
        var manifest = ManifestWith(ExePackage("Pkg1"), ExePackage("Pkg2"));
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Detection = new FalkForge.Engine.Detection.DetectionResult(InstallState.NotInstalled, null, [])
        };

        var step = new PlanStep(new Planner(), channel);
        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var lifecycle = channel.SentEvents
            .Where(e => e is PipelineEvent.PlanPackageBegin or PipelineEvent.PlanPackageComplete)
            .Select(e => e switch
            {
                PipelineEvent.PlanPackageBegin b => $"Begin:{b.PackageId}",
                PipelineEvent.PlanPackageComplete c => $"Complete:{c.PackageId}",
                _ => "?"
            })
            .ToList();

        Assert.Equal(
            ["Begin:Pkg1", "Complete:Pkg1", "Begin:Pkg2", "Complete:Pkg2"],
            lifecycle);
    }

    [Fact]
    public async Task PlanStep_PlanPackage_ReportsPlannedAction()
    {
        var manifest = ManifestWith(ExePackage("Pkg1"));
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Detection = new FalkForge.Engine.Detection.DetectionResult(InstallState.NotInstalled, null, [])
        };

        var step = new PlanStep(new Planner(), channel);
        await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        var begin = channel.SentEvents.OfType<PipelineEvent.PlanPackageBegin>().Single();
        Assert.Equal("Pkg1", begin.PackageId);
        Assert.Equal("Display Pkg1", begin.DisplayName);
        Assert.Equal("Install", begin.PlannedAction);
    }

    [Fact]
    public async Task ApplyStep_EmitsApplyPackageBeginThenComplete_PerPackage_InOrder()
    {
        var manifest = ManifestWith(ExePackage("Pkg1"), ExePackage("Pkg2"));
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        var plan = new InstallPlan
        {
            Actions =
            [
                new PlanAction { PackageId = "Pkg1", ActionType = PlanActionType.Install, Package = manifest.Packages[0] },
                new PlanAction { PackageId = "Pkg2", ActionType = PlanActionType.Install, Package = manifest.Packages[1] }
            ]
        };
        var ctx = new PipelineContext { Manifest = manifest, Plan = plan };

        var step = new ApplyStep(DryRunExecutor(), journalStore, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var lifecycle = channel.SentEvents
            .Where(e => e is PipelineEvent.ApplyPackageBegin or PipelineEvent.ApplyPackageComplete)
            .Select(e => e switch
            {
                PipelineEvent.ApplyPackageBegin b => $"Begin:{b.PackageId}",
                PipelineEvent.ApplyPackageComplete c => $"Complete:{c.PackageId}:{c.Succeeded}",
                _ => "?"
            })
            .ToList();

        Assert.Equal(
            ["Begin:Pkg1", "Complete:Pkg1:True", "Begin:Pkg2", "Complete:Pkg2:True"],
            lifecycle);
    }

    [Fact]
    public async Task ApplyStep_FailedPackage_EmitsApplyPackageCompleteWithSucceededFalse()
    {
        var manifest = ManifestWith(ExePackage("Pkg1"));
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        var plan = new InstallPlan
        {
            Actions =
            [
                new PlanAction { PackageId = "Pkg1", ActionType = PlanActionType.Install, Package = manifest.Packages[0] }
            ]
        };
        var ctx = new PipelineContext { Manifest = manifest, Plan = plan };

        // Failing runner (exit 1602 → Failure).
        var runner = new MockProcessRunner().WithExitCode(1602);
        var executor = new PackageExecutor(
            new MsiExecutor(),
            new MsuExecutor(runner),
            new MspExecutor(runner),
            new BundleExecutor(runner),
            new ExeExecutor(runner),
            new NetRuntimeExecutor(runner));

        var step = new ApplyStep(executor, journalStore, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsFailure);
        var complete = channel.SentEvents.OfType<PipelineEvent.ApplyPackageComplete>().Single();
        Assert.Equal("Pkg1", complete.PackageId);
        Assert.False(complete.Succeeded);
    }

    private static int IndexOfFirst(
        IReadOnlyList<PipelineEvent> events, Func<PipelineEvent, bool> predicate)
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (predicate(events[i])) return i;
        }

        return -1;
    }
}
