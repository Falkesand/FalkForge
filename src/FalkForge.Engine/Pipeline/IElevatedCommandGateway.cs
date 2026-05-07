namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Cross-process elevation port. Hides HMAC handshake, PID+start-time verification,
/// process spawn sequencing, and pipe framing from phase-step code.
/// </summary>
public interface IElevatedCommandGateway : IAsyncDisposable
{
    /// <summary>
    /// Launches the elevated companion process, performs the HMAC handshake, and
    /// verifies PID+start-time. Must be called once before
    /// <see cref="SendCommandAsync"/>.
    /// </summary>
    Task<Result<Unit>> StartAsync(CancellationToken ct);

    /// <summary>
    /// Serializes <paramref name="payload"/> with the given <paramref name="commandName"/>
    /// and sends it to the elevated process. Returns the raw response bytes.
    /// <paramref name="progress"/> receives [0–100] percent during long-running
    /// commands (e.g. MSI install).
    /// </summary>
    Task<Result<byte[]>> SendCommandAsync(
        string commandName,
        byte[] payload,
        IProgress<int>? progress,
        CancellationToken ct);
}
