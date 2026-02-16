namespace FalkForge.Engine.Protocol.Messages;

public sealed class PhaseChangedMessage : EngineMessage
{
    public override MessageType Type => MessageType.PhaseChanged;
    public required EnginePhase Phase { get; init; }
}
