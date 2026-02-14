namespace FalkInstaller.Engine.Protocol.Transport;

public sealed class PipeConnectionOptions
{
    public required string PipeName { get; init; }
    public required byte[] SharedSecret { get; init; }
    public int MaxMessageSize { get; init; } = 1 * 1024 * 1024; // 1MB
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
