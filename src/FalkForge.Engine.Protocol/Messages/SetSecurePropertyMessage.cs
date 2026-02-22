namespace FalkForge.Engine.Protocol.Messages;

public sealed class SetSecurePropertyMessage : EngineMessage
{
    public override MessageType Type => MessageType.SetSecureProperty;
    public required string PropertyName { get; init; }
    public required byte[] SecureValue { get; init; }
}
