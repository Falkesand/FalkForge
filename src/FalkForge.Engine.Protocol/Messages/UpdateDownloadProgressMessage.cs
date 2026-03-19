namespace FalkForge.Engine.Protocol.Messages;

public sealed class UpdateDownloadProgressMessage : EngineMessage
{
    public override MessageType Type => MessageType.UpdateDownloadProgress;
    public required long BytesReceived { get; init; }
    public required long TotalBytes { get; init; }
    public required int PercentComplete { get; init; }
}
