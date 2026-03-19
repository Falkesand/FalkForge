namespace FalkForge.Compiler.Bundle;

public sealed class BundleFeatureModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public bool IsDefault { get; init; } = true;
    public bool IsRequired { get; init; }
    public IReadOnlyList<string> PackageIds { get; init; } = [];
}