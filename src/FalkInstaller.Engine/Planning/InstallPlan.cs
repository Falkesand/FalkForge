namespace FalkInstaller.Engine.Planning;

public sealed class InstallPlan
{
    public required IReadOnlyList<PlanAction> Actions { get; init; }
    public long TotalDiskSpaceRequired { get; init; }
}
