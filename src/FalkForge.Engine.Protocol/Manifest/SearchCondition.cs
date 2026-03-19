namespace FalkForge.Engine.Protocol.Manifest;

public sealed class SearchCondition
{
    public required SearchConditionType Type { get; init; }
    public required string Path { get; init; }
    public string? Value { get; init; }
    public string? Comparison { get; init; }
}
