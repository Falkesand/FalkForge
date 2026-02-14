namespace FalkInstaller.Engine.Protocol.Messages;

public sealed class ErrorMessage : EngineMessage
{
    public override MessageType Type => MessageType.Error;
    public required string Message { get; init; }
    public required ErrorKind Kind { get; init; }
}
