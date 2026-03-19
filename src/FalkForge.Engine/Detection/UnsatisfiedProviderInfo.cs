namespace FalkForge.Engine.Detection;

/// <summary>
/// Information about a dependency provider that does not satisfy the bundle's requirements.
/// </summary>
internal sealed record UnsatisfiedProviderInfo(
    string ProviderKey,
    string? InstalledVersion,
    bool IsMissing);
