using System.Reactive.Subjects;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Ui.Abstractions;

namespace FalkForge.Ui;

public sealed class EngineClient : IInstallerEngine, IAsyncDisposable
{
    private readonly List<FeatureState> _features = [];
    private readonly Subject<EnginePhase> _phase = new();
    private readonly PipeClient _pipe;
    private readonly Subject<InstallProgress> _progress = new();
    private readonly Subject<string> _statusMessage = new();
    private TaskCompletionSource<ApplyResult>? _applyTcs;

    private TaskCompletionSource<DetectResult>? _detectTcs;
    private string _installDirectory = string.Empty;
    private TaskCompletionSource<PlanResult>? _planTcs;
    private TaskCompletionSource<int>? _shutdownTcs;

    // Sticky latch for the "pipe already closed" state. OnPipeClosed only completes whichever
    // TCS is armed *at the moment it fires*; without this latch, a disconnect that races ahead of
    // Detect/Plan/Apply/Shutdown arming its TCS is silently lost (TrySetException has nothing to
    // complete), leaving the caller to hang until its own CancellationToken eventually fires —
    // surfacing an unrelated TaskCanceledException instead of the real cause. Set once, checked
    // by every arm site immediately after publishing its TCS, so no interleaving between
    // "pipe closes" and "caller arms" can drop the signal. The pipe never reconnects once closed,
    // so this flag is never reset.
    private volatile bool _pipeClosed;

    public EngineClient(PipeConnectionOptions options, InstallerManifest manifest)
        : this(options, manifest, logPath: null)
    {
    }

    public EngineClient(PipeConnectionOptions options, InstallerManifest manifest, string? logPath)
    {
        Manifest = manifest;
        LogPath = logPath;
        _pipe = new PipeClient(options, OnMessageReceivedAsync);
        _pipe.PipeClosed += OnPipeClosed;
    }

    public string? LogPath { get; }

    public async ValueTask DisposeAsync()
    {
        _phase.OnCompleted();
        _progress.OnCompleted();
        _statusMessage.OnCompleted();
        await _pipe.DisposeAsync();
        _phase.Dispose();
        _progress.Dispose();
        _statusMessage.Dispose();
    }

    public InstallerManifest Manifest { get; }

    public InstallState DetectedState { get; private set; } = InstallState.NotInstalled;

    public string? InstalledProductVersion { get; private set; }

    public IReadOnlyList<FeatureState> Features => _features.AsReadOnly();

    public string InstallDirectory
    {
        get => _installDirectory;
        set
        {
            _installDirectory = value;
            _ = SendSetInstallDirectoryAsync(value);
        }
    }

    public IObservable<EnginePhase> Phase => _phase;
    public IObservable<InstallProgress> Progress => _progress;
    public IObservable<string> StatusMessage => _statusMessage;

    public async Task<DetectResult> DetectAsync(CancellationToken ct = default)
    {
        _detectTcs = new TaskCompletionSource<DetectResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        FailIfPipeAlreadyClosed(_detectTcs);
        using var registration = ct.Register(() => _detectTcs.TrySetCanceled(ct));

        var sendResult = await _pipe.SendAsync(new RequestDetectMessage(), ct);
        if (sendResult.IsFailure) _detectTcs.TrySetException(new InvalidOperationException(sendResult.Error.Message));

        return await _detectTcs.Task;
    }

    public async Task<PlanResult> PlanAsync(InstallAction action, CancellationToken ct = default)
    {
        _planTcs = new TaskCompletionSource<PlanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        FailIfPipeAlreadyClosed(_planTcs);
        using var registration = ct.Register(() => _planTcs.TrySetCanceled(ct));

        var sendResult = await _pipe.SendAsync(new RequestPlanMessage { Action = action }, ct);
        if (sendResult.IsFailure) _planTcs.TrySetException(new InvalidOperationException(sendResult.Error.Message));

        return await _planTcs.Task;
    }

    public async Task<ApplyResult> ApplyAsync(CancellationToken ct = default)
    {
        _applyTcs = new TaskCompletionSource<ApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        FailIfPipeAlreadyClosed(_applyTcs);
        using var registration = ct.Register(() => _applyTcs.TrySetCanceled(ct));

        var sendResult = await _pipe.SendAsync(new RequestApplyMessage(), ct);
        if (sendResult.IsFailure) _applyTcs.TrySetException(new InvalidOperationException(sendResult.Error.Message));

        return await _applyTcs.Task;
    }

    public void Cancel()
    {
        _ = _pipe.SendAsync(new CancelMessage());
    }

    public void LaunchUpdate()
    {
        _ = _pipe.SendAsync(new LaunchUpdateMessage());
    }

    public void SetProperty(string name, string value)
    {
        _ = SendSetPropertyAsync(name, value);
    }

    public void SetSecureProperty(string name, SensitiveBytes value)
    {
        // Copy into a fresh SensitiveBytes so the caller can safely dispose their copy
        // without affecting the message in flight.
        var copy = SensitiveBytes.FromPlaintext(value.Span);
        _ = SendSetSecurePropertyAsync(name, copy);
    }

    public async Task<int> ShutdownAsync()
    {
        _shutdownTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        FailIfPipeAlreadyClosed(_shutdownTcs);

        var sendResult = await _pipe.SendAsync(new ShutdownRequestMessage());
        if (sendResult.IsFailure) _shutdownTcs.TrySetException(new InvalidOperationException(sendResult.Error.Message));

        return await _shutdownTcs.Task;
    }

    public event Action<string, string?>? UpdateAvailable;
    public event Action<int, long, long>? UpdateDownloadProgress;
    public event Action<string, string?>? UpdateReady;

    public Task<Result<Unit>> ConnectAsync(CancellationToken ct = default)
    {
        return _pipe.ConnectAsync(ct);
    }

    private Task OnMessageReceivedAsync(EngineMessage message)
    {
        switch (message)
        {
            case DetectCompleteMessage detect:
                DetectedState = detect.State;
                InstalledProductVersion = detect.CurrentVersion;
                _features.Clear();
                _features.AddRange(detect.Features);
                _detectTcs?.TrySetResult(new DetectResult(detect.State, detect.CurrentVersion, detect.Features));
                break;

            case PlanCompleteMessage plan:
                _planTcs?.TrySetResult(new PlanResult(plan.PackageIds, plan.TotalDiskSpaceRequired));
                break;

            case ApplyCompleteMessage apply:
                _applyTcs?.TrySetResult(new ApplyResult(apply.ExitCode, apply.ErrorMessage));
                break;

            case ProgressMessage progress:
                _progress.OnNext(progress.Progress);
                break;

            case PhaseChangedMessage phase:
                _phase.OnNext(phase.Phase);
                break;

            case LogMessage log:
                _statusMessage.OnNext(log.Text);
                break;

            case ShutdownResponseMessage shutdown:
                _shutdownTcs?.TrySetResult(shutdown.ExitCode);
                break;

            case UpdateAvailableMessage m:
                UpdateAvailable?.Invoke(m.Version, m.ReleaseNotes);
                break;

            case UpdateDownloadProgressMessage m:
                UpdateDownloadProgress?.Invoke(m.PercentComplete, m.BytesReceived, m.TotalBytes);
                break;

            case UpdateReadyMessage m:
                UpdateReady?.Invoke(m.Version, m.LocalPath);
                break;

            case ErrorMessage error:
                HandleError(error);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Exposes the message handler for testing without requiring a real pipe connection.
    /// </summary>
    internal Task SimulateMessageAsync(EngineMessage message)
    {
        return OnMessageReceivedAsync(message);
    }

    /// <summary>
    ///     Triggers the pipe-closed handler directly for testing.
    ///     Simulates the engine process crashing mid-operation.
    /// </summary>
    internal void SimulatePipeClosed() => OnPipeClosed();

    /// <summary>
    ///     Arms _detectTcs (as DetectAsync would) then waits — used in tests that
    ///     simulate a disconnect while awaiting Detect.
    /// </summary>
    internal async Task SimulateDetectAndWaitForDisconnectAsync(CancellationToken ct)
    {
        _detectTcs = new TaskCompletionSource<DetectResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        FailIfPipeAlreadyClosed(_detectTcs);
        using var reg = ct.Register(() => _detectTcs.TrySetCanceled(ct));
        await _detectTcs.Task;
    }

    /// <summary>
    ///     Arms _applyTcs (as ApplyAsync would) then waits — used in tests that
    ///     simulate a disconnect while awaiting Apply.
    /// </summary>
    internal async Task SimulateApplyAndWaitForDisconnectAsync(CancellationToken ct)
    {
        _applyTcs = new TaskCompletionSource<ApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        FailIfPipeAlreadyClosed(_applyTcs);
        using var reg = ct.Register(() => _applyTcs.TrySetCanceled(ct));
        await _applyTcs.Task;
    }

    private void HandleError(ErrorMessage error)
    {
        var ex = new InvalidOperationException(error.Message);
        _detectTcs?.TrySetException(ex);
        _planTcs?.TrySetException(ex);
        _applyTcs?.TrySetException(ex);
        _shutdownTcs?.TrySetException(ex);
        _statusMessage.OnNext($"Error: {error.Message}");
    }

    private void OnPipeClosed()
    {
        // The engine process crashed or the pipe was closed without sending a proper response.
        // Set the sticky latch FIRST, before touching any TCS: a TCS armed by a Detect/Plan/
        // Apply/Shutdown call that races in right after this method reads the (now-stale) TCS
        // fields still needs to observe the disconnect via FailIfPipeAlreadyClosed. Complete
        // whichever TCS is currently armed so an in-flight caller doesn't hang indefinitely.
        //
        // This thread and an arm-site thread each write one field and read the other
        // (write _pipeClosed / read *Tcs here; write *Tcs / read _pipeClosed there) — the
        // classic store-buffering pattern. `volatile` alone only gives acquire/release, which
        // does not rule out both reads observing stale values under x86 store-buffer
        // reordering. A full StoreLoad fence on both sides (here and in
        // FailIfPipeAlreadyClosed) is required so the write below is globally visible before
        // the read across, guaranteeing at least one side observes the other's update.
        _pipeClosed = true;
        Interlocked.MemoryBarrier();
        var ex = new PipeDisconnectedException();
        _detectTcs?.TrySetException(ex);
        _planTcs?.TrySetException(ex);
        _applyTcs?.TrySetException(ex);
        _shutdownTcs?.TrySetException(ex);
    }

    /// <summary>
    ///     If the pipe already closed before <paramref name="tcs"/> was armed, complete it
    ///     immediately instead of leaving it to rely on the caller's own cancellation token.
    ///     Closes the lost-wakeup window where OnPipeClosed fires against a not-yet-published TCS.
    ///     Must be called immediately after publishing <paramref name="tcs"/> to its field — see
    ///     the fence note in <see cref="OnPipeClosed"/> for why the barrier below is required.
    /// </summary>
    private void FailIfPipeAlreadyClosed<T>(TaskCompletionSource<T> tcs)
    {
        Interlocked.MemoryBarrier();
        if (_pipeClosed) tcs.TrySetException(new PipeDisconnectedException());
    }

    private async Task SendSetInstallDirectoryAsync(string directory)
    {
        if (_pipe.IsConnected) await _pipe.SendAsync(new SetInstallDirectoryMessage { Directory = directory });
    }

    private async Task SendSetPropertyAsync(string name, string value)
    {
        if (_pipe.IsConnected) await _pipe.SendAsync(new SetPropertyMessage { PropertyName = name, Value = value });
    }

    private async Task SendSetSecurePropertyAsync(string name, SensitiveBytes secureValue)
    {
        // The message takes ownership of secureValue. The codec's PostWrite hook disposes it
        // after serialization, zeroing the backing array. No manual zeroing needed here.
        using var msg = new SetSecurePropertyMessage { PropertyName = name, SecureValue = secureValue };
        if (_pipe.IsConnected)
            await _pipe.SendAsync(msg);
    }
}