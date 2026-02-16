namespace FalkForge.Models;

public sealed class MergeModuleModel
{
    public required Guid Id { get; init; }
    public required int Language { get; init; }
    public required Version Version { get; init; }
    public required string Manufacturer { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Components { get; init; } = [];
    public IReadOnlyList<string> Dependencies { get; init; } = [];
}
