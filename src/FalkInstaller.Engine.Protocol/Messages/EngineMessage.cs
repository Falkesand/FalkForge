namespace FalkInstaller.Engine.Protocol.Messages;

public abstract class EngineMessage
{
    public abstract MessageType Type { get; }
    public uint SequenceId { get; init; }
}
