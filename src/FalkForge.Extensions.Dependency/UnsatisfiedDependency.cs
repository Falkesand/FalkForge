namespace FalkForge.Extensions.Dependency;

/// <summary>
/// Represents a dependency consumer whose required provider is either missing or has an unsatisfied version.
/// </summary>
public sealed record UnsatisfiedDependency(
    string ProviderKey,
    string ConsumerKey,
    string? InstalledVersion,
    bool IsMissing);
