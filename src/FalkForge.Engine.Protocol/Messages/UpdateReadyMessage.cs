namespace FalkForge.Engine.Protocol.Messages;

public sealed class UpdateReadyMessage : EngineMessage
{
    public override MessageType Type => MessageType.UpdateReady;
    public required string Version { get; init; }
    public required string LocalPath { get; init; }
}
