namespace FalkForge.Engine.Pipeline;

using FalkForge.Diagnostics;
using FalkForge.Engine.Download;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Live auto-update coordinator owned by the pipeline. Turns a detected update into the
/// per-policy behavior the design documents specify, and remembers the verified cached
/// bundle path so a later <see cref="UiRequest.LaunchUpdate"/> from the UI can run it.
///
/// <para>Policy behavior (per docs/plans/2026-03-06-auto-updater-design.md):</para>
/// <list type="bullet">
///   <item><description><b>NotifyOnly</b> — no download; <see cref="DetectStep"/> already
///   emitted <see cref="PipelineEvent.UpdateAvailable"/>. This service does nothing.</description></item>
///   <item><description><b>DownloadAndPrompt</b> — background-download (delta-first, SHA-256
///   verified), emit <see cref="PipelineEvent.UpdateDownloadProgress"/> then
///   <see cref="PipelineEvent.UpdateReady"/>; launch only when the user later requests it.</description></item>
///   <item><description><b>AutoUpdate</b> — same download flow; launch immediately unless
///   <see cref="ManifestUpdateFeed.PromptBeforeAutoUpdate"/> is set (then behaves like
///   DownloadAndPrompt).</description></item>
/// </list>
///
/// <para>Reuses the fully-tested <see cref="UpdateDownloader"/> for the delta-first download,
/// verification, and policy launch decision. The message sink adapts the downloader's
/// <c>EngineMessage</c> output onto <see cref="IUiChannel"/> <see cref="PipelineEvent"/>s so the
/// pipeline port abstraction is preserved.</para>
/// </summary>
internal sealed class UpdateService
{
    private readonly ManifestUpdateFeed _feed;
    private readonly string _cacheDir;
    private readonly Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, long?, CancellationToken, Task<Result<string>>> _download;
    private readonly IUpdateLauncher _launcher;
    private readonly IUiChannel _channel;
    private readonly IFalkLogger _logger;
    private readonly Func<string, Result<Unit>> _verifyStagedBundle;

    // Written once by the download callback (SendAdaptedMessageAsync, on the download task) and
    // read later by HasReadyUpdate / LaunchReadyUpdate (on the UI-request path). volatile is
    // sufficient: there is a single writer and the path is only ever set, never mutated, so no
    // read-modify-write races exist — only cross-thread visibility needs guaranteeing.
    private volatile string? _readyUpdatePath;

    internal UpdateService(
        ManifestUpdateFeed feed,
        string cacheDir,
        Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, long?, CancellationToken, Task<Result<string>>> download,
        IUpdateLauncher launcher,
        IUiChannel channel,
        IFalkLogger logger,
        Func<string, Result<Unit>>? verifyStagedBundle = null)
    {
        _feed = feed;
        _cacheDir = cacheDir;
        _download = download;
        _launcher = launcher;
        _channel = channel;
        _logger = logger;
        // Secure by default: the real in-process trust gate over the baked trusted set. The PQ
        // incapable-OS fallback (Stage 1) logs through the service's real logger so the degradation
        // is visible in the session log, never silent.
        _verifyStagedBundle = verifyStagedBundle
            ?? (path => StagedUpdateVerifier.VerifyWithBakedTrust(
                path, msg => logger.Warning("UpdateService", msg)));
    }

    /// <summary>
    /// True once a verified update bundle has been downloaded and is ready to launch.
    /// </summary>
    internal bool HasReadyUpdate => _readyUpdatePath is not null;

    /// <summary>
    /// Handles a detected update according to the configured policy. For NotifyOnly this is a
    /// no-op (notification already happened during detection). For DownloadAndPrompt / AutoUpdate
    /// it downloads, emits progress + ready events, and remembers the cached path. The
    /// <see cref="UpdateDownloader"/> performs the AutoUpdate-without-prompt launch itself.
    /// </summary>
    /// <remarks>
    /// Callers must treat this method as potentially throwing; it does not catch exceptions
    /// internally. <see cref="FalkForge.Engine.Phases.DetectStep"/> wraps the call in a
    /// best-effort guard so that a download failure does not block the install session — but
    /// that guard lives in the caller, not here.
    /// </remarks>
    internal async Task HandleUpdateAsync(UpdateInfo update, CancellationToken ct)
    {
        if (_feed.Policy == UpdatePolicy.NotifyOnly)
            return;

        var downloader = new UpdateDownloader(
            _download,
            SendAdaptedMessageAsync,
            _logger,
            _feed.Policy,
            _feed.AllowResumeDownload,
            _launcher,
            _feed.PromptBeforeAutoUpdate,
            _feed.ShowDownloadErrors,
            _verifyStagedBundle);

        await downloader.StartAsync(update, _cacheDir, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Launches the previously-downloaded, verified update bundle. Returns a typed failure
    /// (rather than silently doing nothing) when no update is ready, so the caller can surface
    /// the condition to the UI. The launcher itself enforces Authenticode verification and
    /// path containment, so a refused launch (e.g. signature mismatch) returns its failure here.
    /// </summary>
    internal Result<Unit> LaunchReadyUpdate()
    {
        if (_readyUpdatePath is null)
            return Result<Unit>.Failure(new Error(ErrorKind.EngineError,
                "UPD005: No update has been downloaded and verified; nothing to launch."));

        // Belt-and-suspenders re-verification (C14 Stage 3 FIX 1): the download-time gate already ran
        // before this path was marked ready, but a UI-requested launch happens later and the staged file
        // could have been swapped on disk in the interim. Re-run the in-process trust gate so a launch is
        // never issued for a bundle that does not verify NOW.
        var trust = _verifyStagedBundle(_readyUpdatePath);
        if (!trust.IsSuccess)
        {
            _logger.Warning("UpdateService",
                $"Staged update failed trust verification at launch; refusing to launch: {trust.Error.Message}");
            return Result<Unit>.Failure(new Error(ErrorKind.IntegrityError,
                $"UPD007: The staged update failed trust verification at launch and will not be run: {trust.Error.Message}"));
        }

        return _launcher.Launch(_readyUpdatePath);
    }

    /// <summary>
    /// Adapts an <see cref="EngineMessage"/> emitted by <see cref="UpdateDownloader"/> onto the
    /// pipeline's <see cref="IUiChannel"/> as a <see cref="PipelineEvent"/>. Also intercepts the
    /// ready message to remember the cached path for a later launch request.
    /// </summary>
    private Task SendAdaptedMessageAsync(EngineMessage message, CancellationToken ct)
    {
        switch (message)
        {
            case UpdateDownloadProgressMessage p:
                return _channel.SendAsync(
                    new PipelineEvent.UpdateDownloadProgress(p.BytesReceived, p.TotalBytes, p.PercentComplete),
                    ct);

            case UpdateReadyMessage r:
                _readyUpdatePath = r.LocalPath;
                return _channel.SendAsync(new PipelineEvent.UpdateReady(r.Version, r.LocalPath), ct);

            case ErrorMessage e:
            {
                // Map download errors to a warning log, not a session-terminating Failed event.
                // A best-effort background update failure must never kill the install session.
                var msg = $"Update download failed (non-fatal): [{e.Kind}] {e.Message}";
                _logger.Warning("UpdateService", msg);
                return _channel.SendAsync(new PipelineEvent.Log(LogLevel.Warning, msg), ct);
            }

            default:
                return Task.CompletedTask;
        }
    }
}
