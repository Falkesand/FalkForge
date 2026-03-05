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

    internal UpdateDownloader(
        Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, CancellationToken, Task<Result<string>>> download,
        Func<EngineMessage, CancellationToken, Task> sendMessage,
        IEngineLogger logger,
        UpdatePolicy policy,
        bool allowResume,
        IUpdateLauncher? launcher = null)
    {
        _download = download;
        _sendMessage = sendMessage;
        _logger = logger;
        _policy = policy;
        _allowResume = allowResume;
        _launcher = launcher ?? new NullUpdateLauncher();
    }

    internal async Task StartAsync(UpdateInfo update, string cacheDir, CancellationToken ct)
    {
        var destPath = Path.Combine(cacheDir, $"{update.Sha256}.exe");

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

        var result = await _download(
            update.DownloadUrl, update.Sha256, destPath, progress, _allowResume, ct);

        // Send all accumulated progress messages before the ready notification.
        foreach (var snapshot in progressSnapshots)
        {
            await _sendMessage(snapshot, ct);
        }

        if (!result.IsSuccess)
        {
            _logger.Warning("UpdateDownloader", $"Update download failed: {result.Error.Message}");
            return;
        }

        await _sendMessage(new UpdateReadyMessage
        {
            Version = update.Version,
            LocalPath = result.Value
        }, ct);

        if (_policy == UpdatePolicy.AutoUpdate)
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
