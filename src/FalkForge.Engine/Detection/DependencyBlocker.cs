namespace FalkForge.Engine.Detection;

internal sealed record DependencyBlocker(
    string ProviderKey,
    string? DisplayName,
    IReadOnlyList<string> DependentKeys);
