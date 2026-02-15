namespace FalkForge.Engine.Protocol.Messages;

public sealed class DetectCompleteMessage : EngineMessage
{
    public override MessageType Type => MessageType.DetectComplete;
    public required InstallState State { get; init; }
    public string? CurrentVersion { get; init; }
    public required FeatureState[] Features { get; init; }
}
