namespace FalkForge.Extensions.Dependency;

public sealed class DependencyProviderModel
{
    public required string Key { get; init; }
    public required string Version { get; init; }
    public string? DisplayName { get; init; }
    public string? ComponentRef { get; init; }
}
