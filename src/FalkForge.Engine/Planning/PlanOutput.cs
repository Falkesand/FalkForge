namespace FalkForge.Engine.Planning;

internal sealed class PlanOutput
{
    public required string PlanVersion { get; init; }
    public required string GeneratedAt { get; init; }
    public required PlanActionOutput[] Packages { get; init; }
}
