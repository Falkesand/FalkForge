namespace FalkInstaller.Engine.Protocol.Messages;

public sealed class SetFeatureSelectionMessage : EngineMessage
{
    public override MessageType Type => MessageType.SetFeatureSelection;
    public required string FeatureId { get; init; }
    public required bool IsSelected { get; init; }
}
