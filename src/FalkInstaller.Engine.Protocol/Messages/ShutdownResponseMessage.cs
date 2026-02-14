namespace FalkInstaller.Engine.Protocol.Messages;

public sealed class ShutdownResponseMessage : EngineMessage
{
    public override MessageType Type => MessageType.ShutdownResponse;
    public required int ExitCode { get; init; }
}
