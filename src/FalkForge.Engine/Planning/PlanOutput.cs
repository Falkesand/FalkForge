namespace FalkForge.Engine.Planning;

internal sealed record PlanOutput
{
    public required string PlanVersion { get; init; }
    public required string GeneratedAt { get; init; }
    public required PlanActionOutput[] Packages { get; init; }
}
