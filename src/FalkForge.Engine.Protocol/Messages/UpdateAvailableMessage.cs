namespace FalkForge.Engine.Protocol.Messages;

public sealed class UpdateAvailableMessage : EngineMessage
{
    public override MessageType Type => MessageType.UpdateAvailable;
    public required string Version { get; init; }
    public string? ReleaseNotes { get; init; }
    public required string DownloadUrl { get; init; }
    public string? LocalPath { get; init; }
}
