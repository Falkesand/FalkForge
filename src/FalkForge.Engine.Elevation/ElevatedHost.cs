namespace FalkForge.Engine.Elevation;

using System.Diagnostics;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;

public sealed class ElevatedHost : IAsyncDisposable
{
    private readonly PipeConnectionOptions _pipeOptions;
    private readonly int _parentPid;
    private readonly ElevatedCommandExecutor _executor;
    private PipeClient? _pipe;
    private CancellationTokenSource? _cts;
    private Task? _parentWatchTask;

    public ElevatedHost(PipeConnectionOptions pipeOptions, int parentPid)
    {
        _pipeOptions = pipeOptions;
        _parentPid = parentPid;

        _executor = new ElevatedCommandExecutor(new IElevatedCommand[]
        {
            new MsiInstallCommand(),
            new MsiUninstallCommand(),
            new ServiceInstallCommand(),
            new RegistryWriteCommand(),
            new FileWriteCommand()
        });
    }

    internal ElevatedHost(PipeConnectionOptions pipeOptions, int parentPid, ElevatedCommandExecutor executor)
    {
        _pipeOptions = pipeOptions;
        _parentPid = parentPid;
        _executor = executor;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (!IsParentAlive())
        {
            Console.Error.WriteLine("Parent process not found");
            return 1;
        }

        _parentWatchTask = WatchParentAsync(_cts.Token);

        _pipe = new PipeClient(_pipeOptions, HandleMessageAsync);
        var connectResult = await _pipe.ConnectAsync(_cts.Token);
        if (connectResult.IsFailure)
        {
            Console.Error.WriteLine($"Failed to connect to engine: {connectResult.Error}");
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
        if (message is ElevateExecuteMessage executeMsg)
        {
            var result = _executor.Execute(executeMsg);
            if (_pipe is not null)
                await _pipe.SendAsync(result);
        }
    }

    internal bool IsParentAlive()
    {
        try
        {
            using var process = Process.GetProcessById(_parentPid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
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
