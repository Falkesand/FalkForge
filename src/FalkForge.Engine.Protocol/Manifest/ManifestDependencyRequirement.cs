namespace FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// A dependency requirement that the bundle needs satisfied at runtime.
/// The engine checks whether a matching provider with a compatible version is installed.
/// </summary>
public sealed record ManifestDependencyRequirement(
    string ProviderKey,
    string? MinVersion,
    string? MaxVersion,
    bool MinInclusive,
    bool MaxInclusive);
