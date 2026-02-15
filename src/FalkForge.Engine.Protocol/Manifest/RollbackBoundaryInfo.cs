namespace FalkForge.Engine.Protocol.Manifest;

public sealed class RollbackBoundaryInfo
{
    public required string Id { get; init; }
    public bool Vital { get; init; } = true;
}
