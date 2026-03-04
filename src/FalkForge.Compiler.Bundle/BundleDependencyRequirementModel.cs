namespace FalkForge.Compiler.Bundle;

/// <summary>
///     A dependency requirement that the bundle needs satisfied at runtime.
///     Specifies a provider key and an optional version range.
/// </summary>
public sealed class BundleDependencyRequirementModel
{
    public required string ProviderKey { get; init; }
    public string? MinVersion { get; init; }
    public string? MaxVersion { get; init; }
    public bool MinInclusive { get; init; } = true;
    public bool MaxInclusive { get; init; }
}