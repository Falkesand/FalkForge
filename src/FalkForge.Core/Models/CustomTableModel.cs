namespace FalkForge.Models;

public sealed class CustomTableModel
{
    public required string Name { get; init; }
    public IReadOnlyList<CustomTableColumnModel> Columns { get; init; } = [];
    public IReadOnlyList<Dictionary<string, object?>> Rows { get; init; } = [];
}