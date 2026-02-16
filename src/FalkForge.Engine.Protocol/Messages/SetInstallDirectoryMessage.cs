namespace FalkForge.Engine.Protocol.Messages;

public sealed class SetInstallDirectoryMessage : EngineMessage
{
    public override MessageType Type => MessageType.SetInstallDirectory;
    public required string Directory { get; init; }
}
