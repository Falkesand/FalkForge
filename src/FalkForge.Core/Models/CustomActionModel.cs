namespace FalkForge.Models;

public sealed class CustomActionModel
{
    public required string Id { get; init; }
    public required int Type { get; init; }
    public required string SourceRef { get; init; }
    public string? Target { get; init; }
    public string? Condition { get; init; }
    public int? Sequence { get; init; }
    public string? After { get; init; }
    public string? Before { get; init; }
}
