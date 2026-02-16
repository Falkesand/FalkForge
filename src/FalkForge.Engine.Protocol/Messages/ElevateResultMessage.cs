namespace FalkForge.Engine.Protocol.Messages;

public sealed class ElevateResultMessage : EngineMessage
{
    public override MessageType Type => MessageType.ElevateResult;
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public byte[]? ResultPayload { get; init; }
}
