namespace FalkInstaller.Models;

public sealed class SequenceActionModel
{
    public required string ActionName { get; init; }
    public required SequenceTable Table { get; init; }
    public string? Condition { get; init; }
    public required ActionPosition Position { get; init; }
}
