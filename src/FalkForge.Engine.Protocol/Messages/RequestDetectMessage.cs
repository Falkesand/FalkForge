namespace FalkForge.Engine.Protocol.Messages;

public sealed class RequestDetectMessage : EngineMessage
{
    public override MessageType Type => MessageType.RequestDetect;
}
