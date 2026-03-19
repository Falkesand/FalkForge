namespace FalkForge.Extensions.Dependency;

public sealed class DependencyConsumerModel
{
    public required string ProviderKey { get; init; }
    public required string ConsumerKey { get; init; }
    public string? MinVersion { get; init; }
    public string? MaxVersion { get; init; }
    public bool MinInclusive { get; init; } = true;
    public bool MaxInclusive { get; init; }
    public string? ComponentRef { get; init; }
}