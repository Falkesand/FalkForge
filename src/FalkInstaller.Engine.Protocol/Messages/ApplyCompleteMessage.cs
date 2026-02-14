namespace FalkInstaller.Engine.Protocol.Messages;

public sealed class ApplyCompleteMessage : EngineMessage
{
    public override MessageType Type => MessageType.ApplyComplete;
    public required int ExitCode { get; init; }
    public string? ErrorMessage { get; init; }
}
