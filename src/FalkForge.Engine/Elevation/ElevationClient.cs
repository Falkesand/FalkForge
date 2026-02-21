namespace FalkForge.Engine.Elevation;

using System.Collections.Concurrent;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;

/// <summary>
/// Engine-side client that sends commands to the elevated companion process
/// over a named pipe and correlates responses by sequence ID.
/// </summary>
internal sealed class ElevationClient : IElevationClient
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(10);

    private readonly PipeServer _pipe;
    private readonly TimeSpan _commandTimeout;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<ElevateResultMessage>> _pendingRequests = new();
    private uint _nextSequenceId;
    private volatile bool _disposed;

    /// <summary>
    /// Creates an elevation client that communicates over the given pipe.
    /// The caller must have already registered this instance's <see cref="HandleMessageAsync"/>
    /// as the pipe's message handler (or must route <see cref="ElevateResultMessage"/> to it).
    /// </summary>
    public ElevationClient(PipeServer pipe, TimeSpan? commandTimeout = null)
    {
        _pipe = pipe;
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
    }

    /// <summary>
    /// Handles an incoming message from the elevated process pipe.
    /// Called by the <see cref="PipeServer"/> receive loop on its I/O thread.
    /// Only processes <see cref="ElevateResultMessage"/>; other message types are ignored.
    /// </summary>
    public Task HandleMessageAsync(EngineMessage message)
    {
        if (message is ElevateResultMessage result
            && _pendingRequests.TryRemove(result.SequenceId, out var tcs))
        {
            tcs.TrySetResult(result);
        }

        return Task.CompletedTask;
    }

    public async Task<Result<byte[]>> SendCommandAsync(
        string commandName,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Result<byte[]>.Failure(ErrorKind.ElevationError, "Elevation client has been disposed");

        var sequenceId = Interlocked.Increment(ref _nextSequenceId);

        var tcs = new TaskCompletionSource<ElevateResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[sequenceId] = tcs;

        var message = new ElevateExecuteMessage
        {
            SequenceId = sequenceId,
            CommandName = commandName,
            CommandPayload = payload
        };

        var sendResult = await _pipe.SendAsync(message, cancellationToken);
        if (sendResult.IsFailure)
        {
            _pendingRequests.TryRemove(sequenceId, out _);
            return Result<byte[]>.Failure(ErrorKind.ElevationError,
                $"Failed to send elevation command '{commandName}': {sendResult.Error.Message}");
        }

        // Await response with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_commandTimeout);

        try
        {
            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));

            if (completedTask != tcs.Task)
            {
                // Should not happen because WhenAny with Delay(Infinite) only returns
                // when cancellation fires, but guard anyway.
                _pendingRequests.TryRemove(sequenceId, out _);
                tcs.TrySetCanceled(cancellationToken);
                return Result<byte[]>.Failure(ErrorKind.ElevationError,
                    $"Elevation command '{commandName}' timed out after {_commandTimeout.TotalSeconds:F0}s");
            }

            var result = await tcs.Task;
            return result.Success
                ? Result<byte[]>.Success(result.ResultPayload ?? [])
                : Result<byte[]>.Failure(ErrorKind.ElevationError,
                    result.ErrorMessage ?? $"Elevation command '{commandName}' failed");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Timeout expired (not caller cancellation)
            _pendingRequests.TryRemove(sequenceId, out _);
            tcs.TrySetCanceled(CancellationToken.None);
            return Result<byte[]>.Failure(ErrorKind.ElevationError,
                $"Elevation command '{commandName}' timed out after {_commandTimeout.TotalSeconds:F0}s");
        }
        catch (OperationCanceledException)
        {
            // Caller-requested cancellation
            _pendingRequests.TryRemove(sequenceId, out _);
            tcs.TrySetCanceled(cancellationToken);
            return Result<byte[]>.Failure(ErrorKind.ElevationError,
                $"Elevation command '{commandName}' was cancelled");
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;

        // Complete all pending requests with cancellation so callers don't hang
        foreach (var kvp in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
            {
                tcs.TrySetCanceled();
            }
        }

        // The pipe is not owned by ElevationClient -- it is created and disposed
        // by the ElevatingHandler / EngineHost lifecycle. Do not dispose it here.
        return ValueTask.CompletedTask;
    }
}
