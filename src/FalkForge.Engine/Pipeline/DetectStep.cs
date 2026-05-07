namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Download;
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

    public DetectStep(
        InstallerManifest manifest,
        IRegistry registry,
        IUiChannel uiChannel,
        UpdateChecker? updateChecker = null)
    {
        _manifest = manifest;
        _registry = registry;
        _uiChannel = uiChannel;
        _updateChecker = updateChecker;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        await _uiChannel.SendAsync(
            new PipelineEvent.PhaseChanged(EnginePhase.Detecting), ct);

        ctx.Manifest = _manifest;

        var detector = new PackageDetector(_registry);
        var detection = detector.Detect(_manifest);
        ctx.Detection = detection;

        await _uiChannel.SendAsync(
            new PipelineEvent.Log(LogLevel.Info,
                $"Detection complete: state={detection.State}, version={detection.CurrentVersion ?? "none"}"),
            ct);

        // Update check: best-effort, never fails the detection phase.
        if (_updateChecker is not null && _manifest.UpdateFeed is not null)
        {
            await CheckForUpdateAsync(ctx, ct);
        }

        return Unit.Value;
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
    }
}
