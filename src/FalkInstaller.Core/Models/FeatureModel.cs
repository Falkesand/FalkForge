namespace FalkInstaller.Models;

public sealed class FeatureModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public bool IsRequired { get; init; }
    public bool IsDefault { get; init; } = true;
    public int DisplayLevel { get; init; } = 1;
    public IReadOnlyList<FeatureModel> Children { get; init; } = [];
    public IReadOnlyList<string> ComponentRefs { get; init; } = [];
}
