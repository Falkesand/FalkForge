namespace FalkForge.Models;

public sealed record ComTypeLibModel
{
    public required Guid TypeLibId { get; init; }
    public required Version Version { get; init; }
    public int Language { get; init; }
    public string? Description { get; init; }
    public string? ComponentRef { get; init; }
}
