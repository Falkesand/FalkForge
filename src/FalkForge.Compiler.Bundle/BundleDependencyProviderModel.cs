namespace FalkForge.Compiler.Bundle;

public sealed class BundleDependencyProviderModel
{
    public required string Key { get; init; }
    public required string Version { get; init; }
    public string? DisplayName { get; init; }
}