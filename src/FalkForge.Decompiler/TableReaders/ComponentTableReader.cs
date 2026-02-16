namespace FalkForge.Decompiler.TableReaders;

/// <summary>
/// Reads the Component table from an MSI database.
/// Columns: Component, ComponentId, Directory_, Attributes, Condition, KeyPath
/// </summary>
public static class ComponentTableReader
{
    private static readonly string[] Columns = ["Component", "ComponentId", "Directory_", "Attributes", "Condition", "KeyPath"];

    public sealed class ComponentEntry
    {
        public required string ComponentName { get; init; }
        public string? ComponentId { get; init; }
        public required string DirectoryId { get; init; }
        public int Attributes { get; init; }
        public string? Condition { get; init; }
        public string? KeyPath { get; init; }
    }

    public static Result<List<ComponentEntry>> Read(IMsiTableAccess tableAccess)
    {
        var existsResult = tableAccess.TableExists("Component");
        if (existsResult.IsFailure)
            return Result<List<ComponentEntry>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<List<ComponentEntry>>.Success([]);

        var rowsResult = tableAccess.QueryTable("Component", Columns);
        if (rowsResult.IsFailure)
            return Result<List<ComponentEntry>>.Failure(ErrorKind.Validation, $"DEC003: Failed to read Component table. {rowsResult.Error.Message}");

        var entries = new List<ComponentEntry>();
        foreach (var row in rowsResult.Value)
        {
            _ = int.TryParse(row[3], out var attributes);
            entries.Add(new ComponentEntry
            {
                ComponentName = row[0] ?? string.Empty,
                ComponentId = row[1],
                DirectoryId = row[2] ?? string.Empty,
                Attributes = attributes,
                Condition = row[4],
                KeyPath = row[5]
            });
        }

        return entries;
    }
}
