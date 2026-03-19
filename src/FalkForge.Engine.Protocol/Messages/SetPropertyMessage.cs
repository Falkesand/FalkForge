namespace FalkForge.Engine.Protocol.Messages;

public sealed class SetPropertyMessage : EngineMessage
{
    public override MessageType Type => MessageType.SetProperty;
    public required string PropertyName { get; init; }
    public required string Value { get; init; }
}
