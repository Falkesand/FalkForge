namespace FalkForge.Engine.Protocol.Messages;

public sealed class ElevateProgressMessage : EngineMessage
{
    public override MessageType Type => MessageType.ElevateProgress;
    public required int Percent { get; init; }
}
