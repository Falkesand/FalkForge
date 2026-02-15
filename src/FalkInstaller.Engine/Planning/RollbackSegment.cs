namespace FalkInstaller.Engine.Planning;

public sealed class RollbackSegment
{
    public required string BoundaryId { get; init; }
    public bool Vital { get; init; } = true;
    public List<PlanAction> Actions { get; } = new();
}
