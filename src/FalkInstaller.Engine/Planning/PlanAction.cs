namespace FalkInstaller.Engine.Planning;

using FalkInstaller.Engine.Protocol.Manifest;

public sealed class PlanAction
{
    public required string PackageId { get; init; }
    public required PlanActionType ActionType { get; init; }
    public required PackageInfo Package { get; init; }
    public Dictionary<string, string> Properties { get; init; } = new();
}
