namespace FalkForge.Compiler.Bundle;

public sealed record RollbackBoundaryModel
{
    public required string Id { get; init; }
    public bool Vital { get; init; } = true;
}
