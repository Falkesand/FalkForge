namespace FalkForge.Engine.Protocol.Messages;

public sealed class LicenseMessage : EngineMessage
{
    public override MessageType Type => MessageType.License;
    public required LicenseAction Action { get; init; }
    public string? LicenseContent { get; init; }
}
