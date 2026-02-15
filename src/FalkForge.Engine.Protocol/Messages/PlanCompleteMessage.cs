namespace FalkForge.Engine.Protocol.Messages;

public sealed class PlanCompleteMessage : EngineMessage
{
    public override MessageType Type => MessageType.PlanComplete;
    public required long TotalDiskSpaceRequired { get; init; }
    public required string[] PackageIds { get; init; }
}
