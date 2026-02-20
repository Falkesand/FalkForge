namespace MAS.Models;

public sealed record ParameterEntry(string Name, string Value);

public sealed class ParameterGroup
{
    public required string Header { get; init; }
    public required IReadOnlyList<ParameterEntry> Entries { get; init; }
}
