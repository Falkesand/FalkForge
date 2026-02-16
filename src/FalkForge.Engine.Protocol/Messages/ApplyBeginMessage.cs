namespace FalkForge.Engine.Protocol.Messages;

public sealed class ApplyBeginMessage : EngineMessage
{
    public override MessageType Type => MessageType.ApplyBegin;
    public required int TotalPackages { get; init; }
}
