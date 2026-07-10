namespace FalkForge.Engine.Protocol.Transport;

public sealed class PipeConnectionOptions
{
    public required string PipeName { get; init; }
    public required byte[] SharedSecret { get; init; }
    public int MaxMessageSize { get; init; } = 1 * 1024 * 1024; // 1MB
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional callback invoked when a security-relevant event occurs during handshake
    /// (e.g., HMAC validation failure, client disconnect during handshake).
    /// Use this to log security events to a structured logger.
    /// </summary>
    public Action<string>? OnSecurityEvent { get; init; }

    /// <summary>
    /// When set (client side only), the client verifies via <c>GetNamedPipeServerProcessId</c>
    /// that the pipe server it connected to is owned by this process id — the expected parent
    /// engine PID it was launched by. This binds the SYSTEM-privileged elevated companion to
    /// the exact engine that spawned it, defeating a same-user name-squat where a rogue server
    /// pre-creates the pipe. Mismatch aborts before any message is processed. The PID is the
    /// companion's own out-of-band knowledge (its <c>--parent-pid</c>), never trusted from the wire.
    /// Null (default) skips the check — used by the UI↔Engine channel where PID binding does not apply.
    /// </summary>
    public int? ExpectedServerProcessId { get; init; }
}
