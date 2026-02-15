namespace FalkInstaller.Decompiler.TableReaders;

/// <summary>
/// Reads the File table from an MSI database.
/// Columns: File, Component_, FileName, FileSize, Version, Language, Attributes, Sequence
/// </summary>
public static class FileTableReader
{
    private static readonly string[] Columns = ["File", "Component_", "FileName", "FileSize", "Version", "Language", "Attributes", "Sequence"];

    public sealed class FileEntry
    {
        public required string FileKey { get; init; }
        public required string ComponentRef { get; init; }
        public required string FileName { get; init; }
        public int FileSize { get; init; }
        public string? Version { get; init; }
        public string? Language { get; init; }
        public int Attributes { get; init; }
        public int Sequence { get; init; }
    }

    public static Result<List<FileEntry>> Read(IMsiTableAccess tableAccess)
    {
        var existsResult = tableAccess.TableExists("File");
        if (existsResult.IsFailure)
            return Result<List<FileEntry>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<List<FileEntry>>.Success([]);

        var rowsResult = tableAccess.QueryTable("File", Columns);
        if (rowsResult.IsFailure)
            return Result<List<FileEntry>>.Failure(ErrorKind.Validation, $"DEC003: Failed to read File table. {rowsResult.Error.Message}");

        var entries = new List<FileEntry>();
        foreach (var row in rowsResult.Value)
        {
            _ = int.TryParse(row[3], out var fileSize);
            _ = int.TryParse(row[6], out var attributes);
            _ = int.TryParse(row[7], out var sequence);

            // MSI FileName format: "short|long" — extract the long name
            var fileName = ParseLongFileName(row[2] ?? string.Empty);

            entries.Add(new FileEntry
            {
                FileKey = row[0] ?? string.Empty,
                ComponentRef = row[1] ?? string.Empty,
                FileName = fileName,
                FileSize = fileSize,
                Version = row[4],
                Language = row[5],
                Attributes = attributes,
                Sequence = sequence
            });
        }

        return entries;
    }

    /// <summary>
    /// Parses the MSI FileName column which uses "short|long" format.
    /// Returns the long file name if available, otherwise the short name.
    /// </summary>
    internal static string ParseLongFileName(string msiFileName)
    {
        if (string.IsNullOrEmpty(msiFileName))
            return string.Empty;

        var pipeIndex = msiFileName.IndexOf('|');
        return pipeIndex >= 0 ? msiFileName[(pipeIndex + 1)..] : msiFileName;
    }
}
