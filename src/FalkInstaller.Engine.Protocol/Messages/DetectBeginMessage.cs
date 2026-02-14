namespace FalkInstaller.Engine.Protocol.Messages;

public sealed class DetectBeginMessage : EngineMessage
{
    public override MessageType Type => MessageType.DetectBegin;
}
