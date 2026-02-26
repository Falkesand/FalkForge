namespace FalkForge.Engine.Planning;

internal sealed record PlanActionOutput
{
    public required string PackageId { get; init; }
    public required string Action { get; init; }  // "Install", "Uninstall", "Repair"
}
