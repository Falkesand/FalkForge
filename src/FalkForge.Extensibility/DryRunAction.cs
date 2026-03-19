namespace FalkForge.Extensibility;

public sealed class DryRunAction
{
    public required DryRunActionKind Kind { get; init; }
    public required string Description { get; init; }
}
