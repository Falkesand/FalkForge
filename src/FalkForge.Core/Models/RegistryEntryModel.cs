namespace FalkForge.Models;

public sealed class RegistryEntryModel
{
    public required RegistryRoot Root { get; init; }
    public required string Key { get; init; }
    public string? ValueName { get; init; }
    public object? Value { get; init; }
    public RegistryValueType ValueType { get; init; } = RegistryValueType.String;
    public string? FeatureRef { get; init; }
    public string? ComponentId { get; init; }
}
