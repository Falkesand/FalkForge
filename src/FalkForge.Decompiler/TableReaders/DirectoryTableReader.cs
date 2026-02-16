namespace FalkForge.Decompiler.TableReaders;

/// <summary>
/// Reads the Directory table from an MSI database.
/// Columns: Directory, Directory_Parent, DefaultDir
/// </summary>
public static class DirectoryTableReader
{
    private static readonly string[] Columns = ["Directory", "Directory_Parent", "DefaultDir"];

    /// <summary>
    /// Raw directory entry as read from the MSI Directory table.
    /// </summary>
    public sealed class DirectoryEntry
    {
        public required string DirectoryId { get; init; }
        public string? ParentDirectoryId { get; init; }
        public required string DefaultDir { get; init; }
    }

    public static Result<List<DirectoryEntry>> Read(IMsiTableAccess tableAccess)
    {
        var existsResult = tableAccess.TableExists("Directory");
        if (existsResult.IsFailure)
            return Result<List<DirectoryEntry>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<List<DirectoryEntry>>.Success([]);

        var rowsResult = tableAccess.QueryTable("Directory", Columns);
        if (rowsResult.IsFailure)
            return Result<List<DirectoryEntry>>.Failure(ErrorKind.Validation, $"DEC003: Failed to read Directory table. {rowsResult.Error.Message}");

        var entries = new List<DirectoryEntry>();
        foreach (var row in rowsResult.Value)
        {
            entries.Add(new DirectoryEntry
            {
                DirectoryId = row[0] ?? string.Empty,
                ParentDirectoryId = string.IsNullOrEmpty(row[1]) ? null : row[1],
                DefaultDir = row[2] ?? "."
            });
        }

        return entries;
    }
}
