namespace FalkForge.Ui;

using System.Reactive.Subjects;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Ui.Abstractions;

public sealed class EngineClient : IInstallerEngine, IAsyncDisposable
{
    private readonly PipeClient _pipe;
    private readonly Subject<EnginePhase> _phase = new();
    private readonly Subject<InstallProgress> _progress = new();
    private readonly Subject<string> _statusMessage = new();

    private TaskCompletionSource<DetectResult>? _detectTcs;
    private TaskCompletionSource<PlanResult>? _planTcs;
    private TaskCompletionSource<ApplyResult>? _applyTcs;
    private TaskCompletionSource<int>? _shutdownTcs;

    private readonly List<FeatureState> _features = [];
    private string _installDirectory = string.Empty;

    public EngineClient(PipeConnectionOptions options, InstallerManifest manifest)
    {
        Manifest = manifest;
        _pipe = new PipeClient(options, OnMessageReceivedAsync);
    }

    public InstallerManifest Manifest { get; }

    public InstallState DetectedState { get; private set; } = InstallState.NotInstalled;

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

    public Task<Result<Unit>> ConnectAsync(CancellationToken ct = default)
        => _pipe.ConnectAsync(ct);

    public async Task<DetectResult> DetectAsync(CancellationToken ct = default)
    {
        _detectTcs = new TaskCompletionSource<DetectResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => _detectTcs.TrySetCanceled(ct));

        var sendResult = await _pipe.SendAsync(new RequestDetectMessage(), ct);
        if (sendResult.IsFailure)
        {
            _detectTcs.TrySetException(new InvalidOperationException(sendResult.Error.Message));
        }

        return await _detectTcs.Task;
    }

    public async Task<PlanResult> PlanAsync(InstallAction action, CancellationToken ct = default)
    {
        _planTcs = new TaskCompletionSource<PlanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => _planTcs.TrySetCanceled(ct));

        var sendResult = await _pipe.SendAsync(new RequestPlanMessage { Action = action }, ct);
        if (sendResult.IsFailure)
        {
            _planTcs.TrySetException(new InvalidOperationException(sendResult.Error.Message));
        }

        return await _planTcs.Task;
    }

    public async Task<ApplyResult> ApplyAsync(CancellationToken ct = default)
    {
        _applyTcs = new TaskCompletionSource<ApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => _applyTcs.TrySetCanceled(ct));

        var sendResult = await _pipe.SendAsync(new RequestApplyMessage(), ct);
        if (sendResult.IsFailure)
        {
            _applyTcs.TrySetException(new InvalidOperationException(sendResult.Error.Message));
        }

        return await _applyTcs.Task;
    }

    public void Cancel()
    {
        _ = _pipe.SendAsync(new CancelMessage());
    }

    public void SetProperty(string name, string value)
    {
        _ = SendSetPropertyAsync(name, value);
    }

    public void SetSecureProperty(string name, SensitiveBytes value)
    {
        var copy = value.Span.ToArray();
        _ = SendSetSecurePropertyAsync(name, copy);
    }

    public async Task<int> ShutdownAsync()
    {
        _shutdownTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var sendResult = await _pipe.SendAsync(new ShutdownRequestMessage());
        if (sendResult.IsFailure)
        {
            _shutdownTcs.TrySetException(new InvalidOperationException(sendResult.Error.Message));
        }

        return await _shutdownTcs.Task;
    }

    private Task OnMessageReceivedAsync(EngineMessage message)
    {
        switch (message)
        {
            case DetectCompleteMessage detect:
                DetectedState = detect.State;
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

            case ErrorMessage error:
                HandleError(error);
                break;
        }

        return Task.CompletedTask;
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

    private async Task SendSetInstallDirectoryAsync(string directory)
    {
        if (_pipe.IsConnected)
        {
            await _pipe.SendAsync(new SetInstallDirectoryMessage { Directory = directory });
        }
    }

    private async Task SendSetPropertyAsync(string name, string value)
    {
        if (_pipe.IsConnected)
        {
            await _pipe.SendAsync(new SetPropertyMessage { PropertyName = name, Value = value });
        }
    }

    private async Task SendSetSecurePropertyAsync(string name, byte[] secureValue)
    {
        try
        {
            if (_pipe.IsConnected)
            {
                await _pipe.SendAsync(new SetSecurePropertyMessage
                    { PropertyName = name, SecureValue = secureValue });
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(secureValue);
        }
    }

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
}
