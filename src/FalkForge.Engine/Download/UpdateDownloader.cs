namespace FalkForge.Engine.Download;

using FalkForge.Diagnostics;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;

internal sealed class UpdateDownloader
{
    private readonly Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, long?, CancellationToken, Task<Result<string>>> _download;
    private readonly Func<EngineMessage, CancellationToken, Task> _sendMessage;
    private readonly IFalkLogger _logger;
    private readonly UpdatePolicy _policy;
    private readonly bool _allowResume;
    private readonly IUpdateLauncher _launcher;
    private readonly bool _promptBeforeAutoUpdate;
    private readonly bool _showDownloadErrors;
    private readonly Func<string, Result<Unit>> _verifyStagedBundle;

    internal UpdateDownloader(
        Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, long?, CancellationToken, Task<Result<string>>> download,
        Func<EngineMessage, CancellationToken, Task> sendMessage,
        IFalkLogger logger,
        UpdatePolicy policy,
        bool allowResume,
        IUpdateLauncher? launcher = null,
        bool promptBeforeAutoUpdate = false,
        bool showDownloadErrors = false,
        Func<string, Result<Unit>>? verifyStagedBundle = null)
    {
        _download = download;
        _sendMessage = sendMessage;
        _logger = logger;
        _policy = policy;
        _allowResume = allowResume;
        _launcher = launcher ?? new NullUpdateLauncher();
        _promptBeforeAutoUpdate = promptBeforeAutoUpdate;
        _showDownloadErrors = showDownloadErrors;
        // Secure by default: the real in-process trust gate over the baked trusted set. Callers that
        // do not supply one still get require-signed verification of the downloaded bundle.
        _verifyStagedBundle = verifyStagedBundle ?? StagedUpdateVerifier.VerifyWithBakedTrust;
    }

    internal async Task StartAsync(UpdateInfo update, string cacheDir, CancellationToken ct)
    {
        var destPath = Path.Combine(cacheDir, $"{update.Version}_{update.Sha256}.exe");

        // Collect progress snapshots for sequential delivery after download completes.
        // Progress<T> posts callbacks to a thread pool thread; to guarantee message ordering
        // (progress before UpdateReady) and avoid fire-and-forget races, accumulate here
        // and send after DownloadAsync returns.
        var progressSnapshots = new List<UpdateDownloadProgressMessage>();
        var progress = new SynchronousProgress<(long BytesReceived, long TotalBytes)>(p =>
        {
            var percent = p.TotalBytes > 0
                ? (int)(p.BytesReceived * 100L / p.TotalBytes)
                : 0;
            progressSnapshots.Add(new UpdateDownloadProgressMessage
            {
                BytesReceived = p.BytesReceived,
                TotalBytes = p.TotalBytes,
                PercentComplete = percent
            });
        });

        // Try delta download first if available, then fall back to full download
        Result<string> result;
        if (update.DeltaUrl is not null && update.DeltaSha256 is not null)
        {
            var deltaPath = Path.Combine(cacheDir, $"{update.Version}_{update.DeltaSha256}.delta");
            var deltaResult = await _download(
                update.DeltaUrl, update.DeltaSha256, deltaPath, progress, _allowResume, update.DeltaSize, ct);

            if (deltaResult.IsSuccess)
            {
                _logger.Info("UpdateDownloader", $"Delta update downloaded ({update.DeltaSize ?? 0} bytes).");
                // The downloaded delta bundle is a complete, runnable self-extracting bundle EXE,
                // but its delta payloads are stored as Octodiff delta blobs — they are reconstructed
                // at install time by DeltaApplicator against the previously-installed (base) bundle,
                // which the update launcher passes via --base-bundle. This move only stages the
                // verified bundle EXE at its final cache path; payload reconstruction happens later
                // when the launched bundle runs. If the base bundle is unavailable at that point the
                // launched bundle fails loudly (recover via full download), so this download-time
                // delta→full fallback covers a failed delta *download*, not a failed delta *apply*.
                try
                {
                    File.Move(deltaResult.Value, destPath, overwrite: true);
                    result = destPath;
                }
                catch (IOException ex)
                {
                    _logger.Warning("UpdateDownloader", $"Failed to move delta bundle: {ex.Message}. Falling back to full download.");
                    result = await _download(
                        update.DownloadUrl, update.Sha256, destPath, progress, _allowResume, update.Size, ct);
                }
            }
            else
            {
                _logger.Warning("UpdateDownloader", $"Delta download failed: {deltaResult.Error.Message}. Falling back to full download.");
                result = await _download(
                    update.DownloadUrl, update.Sha256, destPath, progress, _allowResume, update.Size, ct);
            }
        }
        else
        {
            result = await _download(
                update.DownloadUrl, update.Sha256, destPath, progress, _allowResume, update.Size, ct);
        }

        // Send all accumulated progress messages before the ready notification.
        foreach (var snapshot in progressSnapshots)
        {
            await _sendMessage(snapshot, ct);
        }

        if (!result.IsSuccess)
        {
            _logger.Warning("UpdateDownloader", $"Update download failed: {result.Error.Message}");

            if (_showDownloadErrors)
            {
                await _sendMessage(new ErrorMessage
                {
                    Kind = result.Error.Kind,
                    Message = result.Error.Message
                }, ct);
            }

            return;
        }

        // C14 Stage 3 FIX 1 — the already-installed, already-trusted engine verifies the DOWNLOADED
        // bundle IN-PROCESS before advertising it as ready or launching it. The expected SHA-256 came
        // from the (attacker-controllable) update feed, so it proves nothing about authorship; and the
        // downloaded EXE carries its own embedded engine that is free to ignore a --require-signed flag,
        // so relaunching-with-a-flag is trust theater. The trust decision is made HERE, by code the user
        // already trusts, over the staged bytes — never delegated to the artifact it is meant to
        // constrain. A stripped (INT007), untrusted-key-re-signed (INT001), or tampered (INT006) update
        // is discarded and never launched.
        var trust = _verifyStagedBundle(result.Value);
        if (!trust.IsSuccess)
        {
            _logger.Warning("UpdateDownloader",
                $"Downloaded update rejected before launch: {trust.Error.Message}. Discarding staged bundle.");
            TryDeleteStagedBundle(result.Value);

            if (_showDownloadErrors)
                await _sendMessage(new ErrorMessage { Kind = trust.Error.Kind, Message = trust.Error.Message }, ct);

            return; // never advertise or launch an unverified update
        }

        await _sendMessage(new UpdateReadyMessage
        {
            Version = update.Version,
            LocalPath = result.Value
        }, ct);

        if (_policy == UpdatePolicy.AutoUpdate && !_promptBeforeAutoUpdate)
        {
            // Re-verify the staged bundle IMMEDIATELY before launching to shrink the TOCTOU window between
            // the post-download gate above (which advertised the update as ready) and this auto-launch. The
            // staged file could have been swapped on disk in the interim, so a launch is never issued for a
            // bundle that does not verify NOW — matching UpdateService.LaunchReadyUpdate on the UI-request
            // path. The post-download gate remains the primary check; this is belt-and-suspenders.
            var relaunchTrust = _verifyStagedBundle(result.Value);
            if (!relaunchTrust.IsSuccess)
            {
                _logger.Warning("UpdateDownloader",
                    $"Staged update failed trust verification at launch; refusing to launch: {relaunchTrust.Error.Message}");
                return;
            }

            var launchResult = _launcher.Launch(result.Value);
            if (!launchResult.IsSuccess)
                _logger.Warning("UpdateDownloader", $"Update launch failed: {launchResult.Error.Message}");
        }
    }

    /// <summary>
    /// Deletes a staged update bundle that failed the pre-launch trust gate. Best-effort: the security
    /// decision (do not launch) has already been made; a failed delete only leaves an inert, unlaunchable
    /// file in the cache.
    /// </summary>
    private void TryDeleteStagedBundle(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("UpdateDownloader",
                $"Failed to delete rejected staged bundle '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    private sealed class NullUpdateLauncher : IUpdateLauncher
    {
        public Result<Unit> Launch(string updatePath) => Unit.Value;
    }

    /// <summary>
    /// An <see cref="IProgress{T}"/> implementation that invokes the handler synchronously
    /// on the reporting thread, avoiding marshaling to a SynchronizationContext.
    /// This is intentional: progress snapshots are accumulated in-line so they can be
    /// sent in order before the UpdateReady message.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
