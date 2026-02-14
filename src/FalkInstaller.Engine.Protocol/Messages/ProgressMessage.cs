namespace FalkInstaller.Engine.Protocol.Messages;

public sealed class ProgressMessage : EngineMessage
{
    public override MessageType Type => MessageType.Progress;
    public required InstallProgress Progress { get; init; }
}
