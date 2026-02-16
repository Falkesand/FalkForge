namespace FalkForge.Engine.Protocol.Messages;

public sealed class RequestPlanMessage : EngineMessage
{
    public override MessageType Type => MessageType.RequestPlan;
    public required InstallAction Action { get; init; }
}
