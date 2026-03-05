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
}
