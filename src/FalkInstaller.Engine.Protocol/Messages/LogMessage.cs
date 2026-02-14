namespace FalkInstaller.Engine.Protocol.Messages;

public sealed class LogMessage : EngineMessage
{
    public override MessageType Type => MessageType.Log;
    public required string Text { get; init; }
    public required LogLevel Level { get; init; }
}
