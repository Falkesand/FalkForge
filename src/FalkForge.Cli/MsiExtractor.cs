using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;
using FalkForge.Decompiler;

namespace FalkForge.Cli;

/// <summary>
/// Extracts files from an MSI or MSM database, resolving directory paths
/// and decompressing embedded cabinet streams.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MsiExtractor
{
    /// <summary>
    /// Extracts all files from an MSI/MSM to the specified output directory,
    /// preserving the directory structure defined in the Directory table.
    /// </summary>
    /// <returns>The count of extracted files on success.</returns>
    public static Result<int> Extract(string msiPath, string outputDir)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<int>.Failure(dbResult.Error);

        using var db = dbResult.Value;

        // 1. Read Directory table entries
        var dirResult = db.QueryRows(
            "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`", 3);
        if (dirResult.IsFailure)
            return Result<int>.Failure(dirResult.Error);

        var dirEntries = dirResult.Value.Select(r => new DirectoryEntry
        {
            DirectoryId = r[0] ?? string.Empty,
            ParentDirectoryId = string.IsNullOrEmpty(r[1]) ? null : r[1],
            DefaultDir = r[2] ?? "."
        }).ToList();

        var resolver = new DirectoryResolver(dirEntries);

        // 2. Read File + Component tables to map file keys to directory paths
        var fileResult = db.QueryRows(
            "SELECT `File`, `Component_`, `FileName`, `Sequence` FROM `File`", 4);
        if (fileResult.IsFailure)
            return Result<int>.Failure(fileResult.Error);

        var compResult = db.QueryRows(
            "SELECT `Component`, `Directory_` FROM `Component`", 2);
        if (compResult.IsFailure)
            return Result<int>.Failure(compResult.Error);

        var componentDirMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in compResult.Value)
        {
            var compId = row[0];
            var dirId = row[1];
            if (compId is not null && dirId is not null)
                componentDirMap[compId] = dirId;
        }

        // Map: cabinet file key → (resolved directory path, long file name)
        var fileKeyMap = new Dictionary<string, (string DirPath, string FileName)>(StringComparer.Ordinal);
        foreach (var row in fileResult.Value)
        {
            var fileKey = row[0];
            var componentId = row[1];
            var fileNameField = row[2]; // short|long or just name

            if (fileKey is null || componentId is null || fileNameField is null)
                continue;

            if (!componentDirMap.TryGetValue(componentId, out var dirId))
                continue;

            var dirPath = resolver.Resolve(dirId);
            var fileName = ParseLongFileName(fileNameField);
            fileKeyMap[fileKey] = (dirPath, fileName);
        }

        // 3. Read Media table to find embedded cabinets
        var mediaResult = db.QueryRows(
            "SELECT `DiskId`, `LastSequence`, `Cabinet` FROM `Media`", 3);
        if (mediaResult.IsFailure)
            return Result<int>.Failure(mediaResult.Error);

        var totalExtracted = 0;

        // Cumulative uncompressed-byte budget shared across every embedded cabinet, so a
        // multi-cab decompression bomb cannot bypass the cap by spreading bytes over cabs.
        var remainingBudget = MsiStreamName.MaxTotalUncompressedCabinetBytes;

        foreach (var mediaRow in mediaResult.Value)
        {
            var cabinetName = mediaRow[2];
            if (string.IsNullOrEmpty(cabinetName))
                continue;

            // Only handle embedded cabinets (prefixed with #)
            if (!cabinetName.StartsWith('#'))
                continue;

            var streamName = cabinetName[1..]; // Remove '#' prefix

            // The cabinet stream name comes from the attacker-controlled Media.Cabinet column.
            // It is interpolated into an MSI-SQL query, so an embedded quote would inject SQL.
            // Allowlist-validate against the legal MSI stream-name shape (<=62 chars, no quotes
            // or other metacharacters) and skip anything that does not match — defense in depth
            // for a malicious MSI (A03: Injection).
            if (!MsiStreamName.IsValid(streamName))
                continue;

            // 4. Read the cabinet stream from the _Streams table
            var streamResult = db.ReadStream(
                $"SELECT `Name`, `Data` FROM `_Streams` WHERE `Name` = '{streamName}'",
                2, 2);
            if (streamResult.IsFailure)
                continue; // Cabinet may not exist; skip gracefully

            // 5. Decompress via CabinetExtractor, bounding the cumulative uncompressed size so a
            // hostile (zip-bomb) cabinet cannot force unbounded memory allocation.
            using var cabStream = new MemoryStream(streamResult.Value);
            var extractResult = CabinetExtractor.Extract(cabStream, remainingBudget);
            if (extractResult.IsFailure)
                return Result<int>.Failure(extractResult.Error);

            // 6. Write extracted files to output directory
            foreach (var (cabFileKey, fileData) in extractResult.Value)
            {
                string targetDir;
                string targetFileName;

                if (fileKeyMap.TryGetValue(cabFileKey, out var mapping))
                {
                    targetDir = Path.Combine(outputDir, mapping.DirPath);
                    targetFileName = mapping.FileName;
                }
                else
                {
                    // Fallback: place unresolved files in output root
                    targetDir = outputDir;
                    targetFileName = cabFileKey;
                }

                remainingBudget -= fileData.LongLength;

                Directory.CreateDirectory(targetDir);
                var targetPath = Path.Combine(targetDir, targetFileName);
                File.WriteAllBytes(targetPath, fileData);
                totalExtracted++;
            }
        }

        return totalExtracted;
    }

    /// <summary>
    /// Parses the MSI FileName column format "short|long" and returns the long name,
    /// or the whole string if no pipe separator is present.
    /// </summary>
    private static string ParseLongFileName(string fileNameField)
    {
        var pipeIndex = fileNameField.IndexOf('|');
        return pipeIndex >= 0 ? fileNameField[(pipeIndex + 1)..] : fileNameField;
    }
}
