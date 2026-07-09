namespace FalkForge.Engine.Elevation;

using System.Diagnostics;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Platform.Windows;

public sealed class ElevatedHost : IAsyncDisposable
{
    private readonly PipeConnectionOptions _pipeOptions;
    private readonly int _parentPid;
    private readonly DateTime _parentStartTime;
    private readonly ElevatedCommandExecutor _executor;
    private PipeClient? _pipe;
    private CancellationTokenSource? _cts;
    private Task? _parentWatchTask;

    public ElevatedHost(PipeConnectionOptions pipeOptions, int parentPid)
    {
        _pipeOptions = pipeOptions;
        _parentPid = parentPid;
        _parentStartTime = CaptureParentStartTime(parentPid);

        var msiApi = new WindowsMsiApi();
        _executor = new ElevatedCommandExecutor(new IElevatedCommand[]
        {
            new MsiInstallCommand(msiApi),
            new MsiUninstallCommand(msiApi),
            new ServiceInstallCommand(),
            new RegistryWriteCommand(),
            new FileWriteCommand(),
            // C16: advance the ACL-protected anti-downgrade/revocation store elevated (the non-elevated
            // engine cannot write under the restrictive store ACL).
            new TrustStateAdvanceCommand()
        });
    }

    internal ElevatedHost(PipeConnectionOptions pipeOptions, int parentPid, ElevatedCommandExecutor executor)
    {
        _pipeOptions = pipeOptions;
        _parentPid = parentPid;
        _parentStartTime = CaptureParentStartTime(parentPid);
        _executor = executor;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (!IsParentAlive())
        {
            ElevationSecurityLog.SecurityEvent("ParentWatch", $"Parent process not found: pid={_parentPid}");
            await Console.Error.WriteLineAsync("Parent process not found");
            return 1;
        }

        _parentWatchTask = WatchParentAsync(_cts.Token);

        _pipe = new PipeClient(_pipeOptions, HandleMessageAsync);
        var connectResult = await _pipe.ConnectAsync(_cts.Token);
        if (connectResult.IsFailure)
        {
            ElevationSecurityLog.SecurityEvent("Connection", $"Failed to connect to engine pipe: {connectResult.Error}");
            await Console.Error.WriteLineAsync($"Failed to connect to engine: {connectResult.Error}");
            return 1;
        }

        try
        {
            await _parentWatchTask;
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        return 0;
    }

    private async Task HandleMessageAsync(EngineMessage message)
    {
        if (message is SessionStartMessage sessionStart)
        {
            // Propagate the session correlation id to the security log so every
            // subsequent log entry carries the same id as the engine and UI logs.
            ElevationSecurityLog.SetCorrelationId(sessionStart.CorrelationId);
            ElevationSecurityLog.Info("Session", $"Session correlation id set: {sessionStart.CorrelationId:D}");
            return;
        }

        if (message is ElevateExecuteMessage executeMsg)
        {
            Action<int> onProgress = percent =>
            {
                if (_pipe is not null)
                {
                    _ = _pipe.SendAsync(new ElevateProgressMessage
                    {
                        SequenceId = executeMsg.SequenceId,
                        Percent = percent
                    });
                }
            };

            var result = _executor.Execute(executeMsg, onProgress);
            if (_pipe is not null)
                await _pipe.SendAsync(result);
        }
    }

    internal bool IsParentAlive()
    {
        try
        {
            using var process = Process.GetProcessById(_parentPid);
            if (process.HasExited)
                return false;

            // Guard against PID recycling: verify the start time matches
            // the snapshot captured at construction. If a different process
            // now owns this PID, its start time will differ.
            if (process.StartTime != _parentStartTime)
            {
                ElevationSecurityLog.SecurityEvent("ParentWatch",
                    $"PID recycling detected: pid={_parentPid}, expected start={_parentStartTime:O}, actual start={process.StartTime:O}");
                return false;
            }

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static DateTime CaptureParentStartTime(int parentPid)
    {
        try
        {
            using var process = Process.GetProcessById(parentPid);
            return process.StartTime;
        }
        catch (ArgumentException)
        {
            // Process does not exist; return sentinel value.
            // RunAsync will fail on the first IsParentAlive check.
            return DateTime.MinValue;
        }
    }

    private async Task WatchParentAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            if (!IsParentAlive())
            {
                if (_cts is not null)
                    await _cts.CancelAsync();
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
            await _cts.CancelAsync();

        if (_parentWatchTask is not null)
        {
            try { await _parentWatchTask; }
            catch (OperationCanceledException) { }
        }

        if (_pipe is not null)
            await _pipe.DisposeAsync();

        _cts?.Dispose();
    }
}
