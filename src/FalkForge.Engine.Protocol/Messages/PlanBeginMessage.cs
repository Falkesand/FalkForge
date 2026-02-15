namespace FalkForge.Engine.Protocol.Messages;

public sealed class PlanBeginMessage : EngineMessage
{
    public override MessageType Type => MessageType.PlanBegin;
    public required InstallAction Action { get; init; }
}
