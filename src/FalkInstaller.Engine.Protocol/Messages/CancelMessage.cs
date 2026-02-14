namespace FalkInstaller.Engine.Protocol.Messages;

public sealed class CancelMessage : EngineMessage
{
    public override MessageType Type => MessageType.Cancel;
}
