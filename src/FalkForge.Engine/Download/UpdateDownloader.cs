namespace FalkForge.Engine.Download;

using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;

internal sealed class UpdateDownloader
{
    private readonly Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, CancellationToken, Task<Result<string>>> _download;
    private readonly Func<EngineMessage, CancellationToken, Task> _sendMessage;
    private readonly IEngineLogger _logger;
    private readonly UpdatePolicy _policy;
    private readonly bool _allowResume;
    private readonly IUpdateLauncher _launcher;
    private readonly bool _promptBeforeAutoUpdate;
    private readonly bool _showDownloadErrors;

    internal UpdateDownloader(
        Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, CancellationToken, Task<Result<string>>> download,
        Func<EngineMessage, CancellationToken, Task> sendMessage,
        IEngineLogger logger,
        UpdatePolicy policy,
        bool allowResume,
        IUpdateLauncher? launcher = null,
        bool promptBeforeAutoUpdate = false,
        bool showDownloadErrors = false)
    {
        _download = download;
        _sendMessage = sendMessage;
        _logger = logger;
        _policy = policy;
        _allowResume = allowResume;
        _launcher = launcher ?? new NullUpdateLauncher();
        _promptBeforeAutoUpdate = promptBeforeAutoUpdate;
        _showDownloadErrors = showDownloadErrors;
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
                update.DeltaUrl, update.DeltaSha256, deltaPath, progress, _allowResume, ct);

            if (deltaResult.IsSuccess)
            {
                _logger.Info("UpdateDownloader", $"Delta update downloaded ({update.DeltaSize ?? 0} bytes).");
                // The delta bundle is a complete runnable bundle; rename to final path
                try
                {
                    File.Move(deltaResult.Value, destPath, overwrite: true);
                    result = destPath;
                }
                catch (IOException ex)
                {
                    _logger.Warning("UpdateDownloader", $"Failed to move delta bundle: {ex.Message}. Falling back to full download.");
                    result = await _download(
                        update.DownloadUrl, update.Sha256, destPath, progress, _allowResume, ct);
                }
            }
            else
            {
                _logger.Warning("UpdateDownloader", $"Delta download failed: {deltaResult.Error.Message}. Falling back to full download.");
                result = await _download(
                    update.DownloadUrl, update.Sha256, destPath, progress, _allowResume, ct);
            }
        }
        else
        {
            result = await _download(
                update.DownloadUrl, update.Sha256, destPath, progress, _allowResume, ct);
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

        await _sendMessage(new UpdateReadyMessage
        {
            Version = update.Version,
            LocalPath = result.Value
        }, ct);

        if (_policy == UpdatePolicy.AutoUpdate && !_promptBeforeAutoUpdate)
        {
            var launchResult = _launcher.Launch(result.Value);
            if (!launchResult.IsSuccess)
                _logger.Warning("UpdateDownloader", $"Update launch failed: {launchResult.Error.Message}");
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
