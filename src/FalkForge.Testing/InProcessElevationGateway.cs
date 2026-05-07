namespace FalkForge.Testing;

using FalkForge.Engine.Pipeline;

/// <summary>
/// In-process <see cref="IElevatedCommandGateway"/> for tests. No process is
/// spawned; commands are dispatched directly to a registered handler delegate.
/// Useful for unit-testing phase steps that issue elevation commands without
/// a real elevated process.
/// </summary>
public sealed class InProcessElevationGateway : IElevatedCommandGateway
{
    private readonly Func<string, byte[], IProgress<int>?, CancellationToken, Task<Result<byte[]>>> _handler;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// All (commandName, payload) pairs sent via <see cref="SendCommandAsync"/>.
    /// </summary>
    public List<(string CommandName, byte[] Payload)> SentCommands { get; } = [];

    /// <summary>
    /// Creates an in-process gateway whose <see cref="SendCommandAsync"/> delegates
    /// to <paramref name="handler"/>. Pass <c>null</c> to use a default handler that
    /// always returns an empty success payload.
    /// </summary>
    public InProcessElevationGateway(
        Func<string, byte[], IProgress<int>?, CancellationToken, Task<Result<byte[]>>>? handler = null)
    {
        _handler = handler ?? ((_, _, _, _) =>
            Task.FromResult(Result<byte[]>.Success(Array.Empty<byte>())));
    }

    /// <summary>
    /// Convenience factory: creates a gateway that returns
    /// <paramref name="response"/> bytes for every command.
    /// </summary>
    public static InProcessElevationGateway AlwaysSucceeds(byte[]? response = null) =>
        new((_, _, _, _) =>
            Task.FromResult(Result<byte[]>.Success(response ?? [])));

    /// <summary>
    /// Convenience factory: creates a gateway that always returns
    /// <see cref="ErrorKind.ElevationError"/> with the given message.
    /// </summary>
    public static InProcessElevationGateway AlwaysFails(string message) =>
        new((_, _, _, _) =>
            Task.FromResult(Result<byte[]>.Failure(ErrorKind.ElevationError, message)));

    /// <inheritdoc/>
    public Task<Result<Unit>> StartAsync(CancellationToken ct)
    {
        if (_disposed)
            return Task.FromResult(
                Result<Unit>.Failure(ErrorKind.ElevationError, "Gateway disposed."));
        _started = true;
        return Task.FromResult(Result<Unit>.Success(Unit.Value));
    }

    /// <inheritdoc/>
    public async Task<Result<byte[]>> SendCommandAsync(
        string commandName,
        byte[] payload,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        if (_disposed || !_started)
            return Result<byte[]>.Failure(ErrorKind.ElevationError,
                "InProcessElevationGateway not started.");

        SentCommands.Add((commandName, payload));
        return await _handler(commandName, payload, progress, ct);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return default;
    }
}
