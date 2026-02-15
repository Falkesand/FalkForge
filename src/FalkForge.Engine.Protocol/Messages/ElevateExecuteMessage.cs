namespace FalkForge.Engine.Protocol.Messages;

public sealed class ElevateExecuteMessage : EngineMessage
{
    public override MessageType Type => MessageType.ElevateExecute;
    public required string CommandName { get; init; }
    public required byte[] CommandPayload { get; init; }
}
