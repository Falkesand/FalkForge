namespace FalkForge.Engine.Pipeline;

using System.Diagnostics;
using FalkForge.Diagnostics;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Dependencies;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.RestartManager;
using FalkForge.Platform;
using FalkForge.Platform.Dependencies;

/// <summary>
/// Apply phase step. Executes each <see cref="PlanAction"/> in the current plan
/// using <see cref="PackageExecutor"/>, journals each installed package for
/// potential rollback, and reports progress via <see cref="IUiChannel"/>.
/// Supports dry-run simulation (from <see cref="PipelineContext.IsDryRun"/>) and
/// optional Restart Manager integration (from <see cref="PipelineContext.RestartManager"/>).
/// </summary>
internal sealed class ApplyStep : IApplyStep
{
    private readonly PackageExecutor _executor;
    private readonly IRollbackJournalStore _journalStore;
    private readonly IUiChannel _uiChannel;
    private readonly IRegistry? _registry;

    public ApplyStep(
        PackageExecutor executor,
        IRollbackJournalStore journalStore,
        IUiChannel uiChannel,
        IRegistry? registry = null)
    {
        _executor = executor;
        _journalStore = journalStore;
        _uiChannel = uiChannel;
        _registry = registry;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        var startTs = Stopwatch.GetTimestamp();
        try
        {
            return await ExecuteCoreAsync(ctx, ct);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
            EngineMeter.RecordPhaseTransition(EnginePhase.Applying, elapsedMs);
        }
    }

    private async Task<Result<Unit>> ExecuteCoreAsync(PipelineContext ctx, CancellationToken ct)
    {
        await _uiChannel.SendAsync(
            new PipelineEvent.PhaseChanged(EnginePhase.Applying), ct);

        if (ctx.Plan is null)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "ApplyStep: plan not populated — PlanStep must run first.");

        // Integrity gate: before executing any payload, prove the manifest's signed
        // payload hashes are authentic. An unsigned manifest passes through (backward
        // compatible); a tampered/forged one aborts the install with a SecurityError
        // before a single package runs. Independent of Authenticode.
        if (ctx.Manifest is not null)
        {
            // PQ-hybrid Stage 1: collect the incapable-OS classical-fallback warnings emitted by
            // the gate and forward them to the UI channel log after the verify, so the "PQ skipped
            // due to OS" degradation is loud in the session log, never silent.
            var pqFallbackWarnings = new List<string>();
            var integrity = PayloadIntegrityGate.Verify(
                ctx.Manifest, ctx.IntegrityTrustPolicy, pqFallbackWarnings.Add);
            foreach (var warning in pqFallbackWarnings)
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Log(LogLevel.Warning, warning), ct);
            }

            if (integrity.IsFailure)
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Log(LogLevel.Error,
                        $"Integrity verification failed — installation aborted: {integrity.Error.Message}"),
                    ct);
                return integrity;
            }
        }

        // Payload path resolution (bug #56): the manifest's SourcePath is the BUILD machine's absolute
        // path, meaningless on any other box. When the bootstrapper forwarded where it extracted the
        // bundle's payloads (ctx.PayloadRoot), resolve every action to its extracted location under that
        // root — with the same containment guard as PreUIPrerequisiteInstaller — and stash it on the
        // action so every executor (direct + elevated) installs the file that actually exists here. Must
        // run BEFORE Restart Manager registration so the resolved paths reach RM too. A crafted package
        // id that escapes the root aborts the install loudly. Null root = the --manifest / plan /
        // offline-layout path where SourcePath is authoritative — untouched.
        if (ctx.PayloadRoot is not null)
        {
            var resolveResult = ResolvePayloadPaths(ctx.Plan, ctx.PayloadRoot);
            if (resolveResult.IsFailure)
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Log(LogLevel.Error,
                        $"Payload path resolution failed — installation aborted: {resolveResult.Error.Message}"),
                    ct);
                return resolveResult;
            }
        }

        // Restart Manager: start session and shut down conflicting processes before apply.
        // Best-effort — RM failures are logged but never abort the installation.
        var rmShutdownPerformed = false;
        if (ctx.RestartManager is not null)
        {
            rmShutdownPerformed = await StartRestartManagerAsync(ctx, ct);
        }

        try
        {
            return await ExecuteActionsAsync(ctx, ct);
        }
        finally
        {
            // Restart Manager: always restart processes and end session, even on failure.
            if (ctx.RestartManager is not null)
            {
                FinishRestartManager(ctx, rmShutdownPerformed);
            }
        }
    }

    private async Task<Result<Unit>> ExecuteActionsAsync(PipelineContext ctx, CancellationToken ct)
    {
        var actions = ctx.Plan!.Actions;
        var total = actions.Count;

        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var action = actions[i];
            var percent = total > 0 ? (int)((double)i / total * 100) : 0;

            await _uiChannel.SendAsync(
                new PipelineEvent.Progress(percent,
                    $"{action.ActionType} {action.Package.DisplayName ?? action.PackageId}"),
                ct);

            // Per-package apply notification: emitted immediately before the package's installer
            // runs, in execution (chain) order. Observational.
            await _uiChannel.SendAsync(
                new PipelineEvent.ApplyPackageBegin(
                    action.PackageId, action.Package.DisplayName ?? action.PackageId),
                ct);

            // Build per-package progress relay that maps [0–100] to the slice
            // of overall progress this package occupies.
            var sliceStart = percent;
            var sliceEnd = total > 0 ? (int)((double)(i + 1) / total * 100) : 100;
            var uiChannel = _uiChannel;
            var packageProgress = new Progress<int>(p =>
            {
                var mapped = sliceStart + (int)((double)p / 100 * (sliceEnd - sliceStart));
                // Fire-and-forget; progress events are advisory
                _ = uiChannel.SendAsync(
                    new PipelineEvent.Progress(mapped, null),
                    CancellationToken.None);
            });

            var result = await _executor.ExecuteAsync(
                action, ctx.IsDryRun, ctx.DryRunLogPath, ct, packageProgress);

            // Per-package apply completion notification, emitted before any early-return on
            // failure so the UI always sees the outcome of the package it was told had begun.
            await _uiChannel.SendAsync(
                new PipelineEvent.ApplyPackageComplete(
                    action.PackageId, action.Package.DisplayName ?? action.PackageId, result.IsSuccess),
                ct);

            if (result.IsFailure)
                return Result<Unit>.Failure(result.Error);

            // Skip journaling in dry-run mode — nothing was actually installed.
            if (!ctx.IsDryRun)
            {
                var journalEntry = BuildJournalEntry(action);
                if (journalEntry is not null)
                {
                    var appendResult = _journalStore.Append(journalEntry);
                    if (appendResult.IsFailure)
                        return Result<Unit>.Failure(appendResult.Error);
                }
            }

            if (result.Value.Behavior
                    is ExitCodeBehavior.RebootRequired
                    or ExitCodeBehavior.ScheduleReboot)
            {
                ctx.RebootRequired = true;
            }

            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Info,
                    $"Completed {action.ActionType} of '{action.PackageId}'"),
                ct);
        }

        await _uiChannel.SendAsync(new PipelineEvent.Progress(100, null), ct);

        // Dependency enforcement write side: after every action has actually run (never in dry-run, and
        // never reached at all if any action above failed and returned early), register this bundle's
        // declared providers/consumers on install or unregister its own consumer entries on uninstall.
        // Best-effort — a persistence failure here must never fail an otherwise-successful install/
        // uninstall (Apply already ran; failing now would report a broken outcome for work that
        // genuinely succeeded).
        if (!ctx.IsDryRun && _registry is not null)
        {
            await RegisterOrUnregisterDependenciesAsync(ctx, ct);
        }

        // C16: on the require-signed update path, advance the anti-downgrade/revocation store now that the
        // apply is verified AND completed (advancing after success, never before, prevents an attacker
        // priming the epoch — a forged epoch fails signature verification before apply). The write is
        // forwarded to the elevated companion (the store ACL denies a non-elevated write); if elevation was
        // unavailable this run, the coordinator says so loudly and does not claim protection it did not record.
        if (ctx.AdvanceTrustStoreOnVerifiedApply && !ctx.IsDryRun)
        {
            var envelope = ctx.Manifest?.ManifestSignature is { } signature
                ? IntegrityEnvelopeCodec.Parse(signature)
                : null;
            await TrustStoreAdvanceCoordinator.AdvanceAsync(
                envelope, ctx.ElevationGateway, _uiChannel, ct);
        }

        return Unit.Value;
    }

    /// <summary>
    /// Starts a Restart Manager session, registers package paths, and shuts down
    /// conflicting processes. Returns true if shutdown was performed (so they can
    /// be restarted in the finally block). Best-effort: never throws.
    /// </summary>
    private async Task<bool> StartRestartManagerAsync(PipelineContext ctx, CancellationToken ct)
    {
        var rm = ctx.RestartManager!;
        try
        {
            var startResult = rm.StartSession();
            if (startResult.IsFailure)
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Log(LogLevel.Warning,
                        $"Restart Manager session start failed: {startResult.Error.Message}"),
                    ct);
                return false;
            }

            // Register all package source paths
            var paths = CollectSourcePaths(ctx.Plan!);
            if (paths.Count > 0)
            {
                var regResult = rm.RegisterResources(paths);
                if (regResult.IsFailure)
                {
                    await _uiChannel.SendAsync(
                        new PipelineEvent.Log(LogLevel.Warning,
                            $"Restart Manager resource registration failed: {regResult.Error.Message}"),
                        ct);
                    return false;
                }
            }

            var affectedResult = rm.GetAffectedProcesses();
            if (affectedResult.IsFailure || affectedResult.Value.Count == 0)
                return false;

            var shutdownResult = rm.ShutdownProcesses();
            if (shutdownResult.IsFailure)
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Log(LogLevel.Warning,
                        $"Restart Manager process shutdown failed: {shutdownResult.Error.Message}"),
                    ct);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Warning,
                    $"Restart Manager unexpected error: {ex.Message}"),
                ct);
            return false;
        }
    }

    /// <summary>
    /// Restarts previously shut down processes and ends the RM session. Always called
    /// in a finally block regardless of installation outcome.
    /// </summary>
    private static void FinishRestartManager(PipelineContext ctx, bool shutdownPerformed)
    {
        var rm = ctx.RestartManager!;
        try
        {
            if (shutdownPerformed)
                rm.RestartProcesses();
        }
        finally
        {
            rm.EndSession();
        }
    }

    /// <summary>
    /// Resolves each action's payload to its extracted location under <paramref name="payloadRoot"/>
    /// (see <see cref="PayloadPathResolver"/>) and records it on
    /// <see cref="PlanAction.ResolvedSourcePath"/>. Fails loud (SecurityError) on a package id that
    /// escapes the root, so a tampered manifest cannot redirect the install to an out-of-cache file.
    /// </summary>
    private static Result<Unit> ResolvePayloadPaths(InstallPlan plan, string payloadRoot)
    {
        foreach (var action in plan.Actions)
        {
            var resolved = PayloadPathResolver.Resolve(payloadRoot, action.Package.Id);
            if (resolved.IsFailure)
                return Result<Unit>.Failure(resolved.Error);

            action.ResolvedSourcePath = resolved.Value;
        }

        return Unit.Value;
    }

    private static List<string> CollectSourcePaths(InstallPlan plan)
    {
        var paths = new List<string>(plan.Actions.Count);
        foreach (var action in plan.Actions)
        {
            // Use the extraction-resolved path when present (distributed bundle) so Restart Manager
            // registers the file that actually exists on this machine; else the manifest SourcePath.
            var sourcePath = action.EffectiveSourcePath;
            if (!string.IsNullOrEmpty(sourcePath))
                paths.Add(sourcePath);
        }

        return paths;
    }

    /// <summary>
    /// Registers this bundle's declared dependency providers/consumers on install, or unregisters ONLY
    /// its own consumer entries on uninstall (Repair/Modify leave dependency registrations untouched — no
    /// package is being newly installed or removed). <see cref="InstallScope.PerUser"/> writes directly
    /// through <paramref name="ctx"/>'s injected <see cref="IRegistry"/> to <c>HKCU</c> — no elevation
    /// needed. <see cref="InstallScope.PerMachine"/> forwards the same intent to the elevated companion's
    /// <c>DependencyRegistration</c> command, which writes <c>HKLM</c>; when no elevation gateway is
    /// available this run, the write is skipped with a warning rather than failing the install. Any
    /// exception (registry access denied, elevated round-trip failure) is caught and logged as a warning —
    /// this method must never turn an already-successful apply into a failure.
    /// </summary>
    private async Task RegisterOrUnregisterDependenciesAsync(PipelineContext ctx, CancellationToken ct)
    {
        var manifest = ctx.Manifest;
        if (manifest is null)
            return;

        if (manifest.DependencyProviders.Length == 0 && manifest.DependencyConsumers.Length == 0)
            return;

        var action = ctx.PlanRequest?.Action;
        if (action is not (InstallAction.Install or InstallAction.Uninstall))
            return;

        try
        {
            if (manifest.Scope == InstallScope.PerUser)
            {
                var registrar = new DependencyRegistrar(_registry!);
                var root = DependencyRegistrationPaths.WriteRootForScope(InstallScope.PerUser);

                if (action == InstallAction.Install)
                {
                    foreach (var provider in manifest.DependencyProviders)
                        registrar.RegisterProvider(root, provider.Key, provider.Version, provider.DisplayName);

                    foreach (var consumer in manifest.DependencyConsumers)
                        registrar.RegisterConsumer(root, consumer.ProviderKey, consumer.ConsumerKey, manifest.BundleId);
                }
                else
                {
                    foreach (var consumer in manifest.DependencyConsumers)
                        registrar.UnregisterConsumer(root, consumer.ProviderKey, consumer.ConsumerKey);
                }

                return;
            }

            // PerMachine: forward to the elevated companion.
            if (ctx.ElevationGateway is null)
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Log(LogLevel.Warning,
                        "Dependency registration skipped: no elevation available for this per-machine " +
                        "install. Provider/consumer registration state was NOT updated."),
                    ct);
                return;
            }

            var opcode = action == InstallAction.Install
                ? DependencyRegistrationOpcode.Register
                : DependencyRegistrationOpcode.Unregister;
            var providers = action == InstallAction.Install
                ? manifest.DependencyProviders
                : [];
            var payload = DependencyRegistrationPayload.Serialize(
                opcode, manifest.BundleId, providers, manifest.DependencyConsumers);

            var sendResult = await ctx.ElevationGateway.SendCommandAsync(
                "DependencyRegistration", payload, null, ct);
            if (sendResult.IsFailure)
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Log(LogLevel.Warning,
                        $"Dependency registration failed (install result unaffected): {sendResult.Error.Message}"),
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Warning,
                    $"Dependency registration failed (install result unaffected): {ex.Message}"),
                ct);
        }
    }

    private static JournalEntry? BuildJournalEntry(PlanAction action)
    {
        // Only journal installs — uninstall/repair don't need rollback entries
        if (action.ActionType != PlanActionType.Install)
            return null;

        var productCode = action.Properties.GetValueOrDefault("ProductCode")
                          ?? action.Package.Properties.GetValueOrDefault("ProductCode");

        if (productCode is not null)
        {
            return new JournalEntry
            {
                EntryType = JournalEntryType.MsiInstalled,
                Description = $"MSI installed: {action.Package.DisplayName ?? action.PackageId}",
                PackageId = action.PackageId,
                PackageType = action.Package.Type.ToString(),
                ProductCode = productCode
            };
        }

        return new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = $"Package installed: {action.Package.DisplayName ?? action.PackageId}",
            PackageId = action.PackageId,
            PackageType = action.Package.Type.ToString()
        };
    }
}
