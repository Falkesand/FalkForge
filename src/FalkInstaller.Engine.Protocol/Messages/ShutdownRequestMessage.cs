namespace FalkInstaller.Engine.Protocol.Messages;

public sealed class ShutdownRequestMessage : EngineMessage
{
    public override MessageType Type => MessageType.ShutdownRequest;
}
