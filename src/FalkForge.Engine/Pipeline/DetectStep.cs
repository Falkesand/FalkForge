namespace FalkForge.Engine.Pipeline;

using System.Diagnostics;
using FalkForge.Diagnostics;
using FalkForge.Engine.Detection;
using FalkForge.Engine.Download;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

/// <summary>
/// Detect phase step. Loads the manifest (from embedded bytes or a layout store),
/// runs <see cref="PackageDetector"/> + dependency detection, emits
/// <see cref="PipelineEvent.PhaseChanged"/> for <see cref="EnginePhase.Detecting"/>,
/// and populates <see cref="PipelineContext.Detection"/>.
/// When the manifest has an <see cref="InstallerManifest.UpdateFeed"/> and an optional
/// <see cref="UpdateChecker"/> is injected, also checks for updates and emits
/// <see cref="PipelineEvent.UpdateAvailable"/> when a newer version is found.
/// </summary>
internal sealed class DetectStep : IDetectStep
{
    private readonly InstallerManifest _manifest;
    private readonly IRegistry _registry;
    private readonly IUiChannel _uiChannel;
    private readonly UpdateChecker? _updateChecker;
    private readonly UpdateService? _updateService;

    public DetectStep(
        InstallerManifest manifest,
        IRegistry registry,
        IUiChannel uiChannel,
        UpdateChecker? updateChecker = null,
        UpdateService? updateService = null)
    {
        _manifest = manifest;
        _registry = registry;
        _uiChannel = uiChannel;
        _updateChecker = updateChecker;
        _updateService = updateService;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        // Capture start timestamp so we always record phase duration, even on
        // exception paths. Stopwatch.GetTimestamp avoids the ~24-byte Stopwatch
        // allocation (Gate 6: zero-waste).
        var startTs = Stopwatch.GetTimestamp();
        try
        {
            await _uiChannel.SendAsync(
                new PipelineEvent.PhaseChanged(EnginePhase.Detecting), ct);

            ctx.Manifest = _manifest;

            var detector = new PackageDetector(_registry);
            var detection = detector.Detect(_manifest);
            ctx.Detection = detection;

            // Per-package detection notifications, emitted in manifest chain order between the
            // overall Detecting phase-change and the aggregate detection log. Observational.
            foreach (var package in detector.DetectPackageStates(_manifest))
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.DetectPackageComplete(package.PackageId, package.State, package.Version),
                    ct);
            }

            // Related-bundle detection: populate the context (consumed by the planner) and emit a
            // per-related-bundle notification for each match. Best-effort — a detection failure is
            // logged and does not fail the detect phase.
            var relatedResult = detector.DetectRelatedBundles(_manifest);
            if (relatedResult.IsSuccess)
            {
                ctx.RelatedBundles = relatedResult.Value;
                foreach (var bundle in relatedResult.Value)
                {
                    await _uiChannel.SendAsync(
                        new PipelineEvent.DetectRelatedBundle(
                            bundle.BundleId, bundle.Relation, bundle.InstalledVersion),
                        ct);
                }
            }
            else
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Log(LogLevel.Warning,
                        $"Related-bundle detection failed and was skipped: {relatedResult.Error.Message}"),
                    ct);
            }

            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Info,
                    $"Detection complete: state={detection.State}, version={detection.CurrentVersion ?? "none"}"),
                ct);

            // Update check: best-effort, never fails the detection phase. An unexpected throw from
            // the update flow (e.g. a download blow-up) must be swallowed and logged so detection —
            // and therefore the install — still proceeds. Cancellation is the one exception we must
            // honor: it has to propagate so a user-cancelled session actually stops.
            if (_updateChecker is not null && _manifest.UpdateFeed is not null)
            {
                try
                {
                    await CheckForUpdateAsync(ctx, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await _uiChannel.SendAsync(
                        new PipelineEvent.Log(LogLevel.Warning,
                            $"Update check failed unexpectedly and was skipped: {ex.Message}"),
                        ct);
                }
            }

            return Unit.Value;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
            EngineMeter.RecordPhaseTransition(EnginePhase.Detecting, elapsedMs);
        }
    }

    private async Task CheckForUpdateAsync(PipelineContext ctx, CancellationToken ct)
    {
        var feed = _manifest.UpdateFeed!;
        var currentVersion = _manifest.Version;

        var checkResult = await _updateChecker!.CheckForUpdateAsync(
            feed, _manifest.BundleId, currentVersion, ct);

        if (checkResult.IsFailure)
        {
            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Warning,
                    $"Update check failed: {checkResult.Error.Message}"),
                ct);
            return;
        }

        ctx.AvailableUpdate = checkResult.Value;

        if (checkResult.Value.Update is null)
        {
            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Info, "No update available"),
                ct);
            return;
        }

        var update = checkResult.Value.Update;
        await _uiChannel.SendAsync(
            new PipelineEvent.UpdateAvailable(
                update.Version,
                update.DownloadUrl,
                ReleaseNotes: null),
            ct);

        await _uiChannel.SendAsync(
            new PipelineEvent.Log(LogLevel.Info,
                $"Update available: {update.Version} at {update.DownloadUrl}"),
            ct);

        // For DownloadAndPrompt / AutoUpdate policies, kick off the background download now.
        // UpdateService is a no-op for NotifyOnly (notification already happened above) and
        // never throws — an update failure must not block detection or the install.
        if (_updateService is not null)
        {
            await _updateService.HandleUpdateAsync(update, ct).ConfigureAwait(false);
        }
    }
}
