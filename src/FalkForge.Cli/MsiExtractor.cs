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
                string relativeDir;
                string targetFileName;

                if (fileKeyMap.TryGetValue(cabFileKey, out var mapping))
                {
                    relativeDir = mapping.DirPath;
                    targetFileName = mapping.FileName;
                }
                else
                {
                    // Fallback: place unresolved files in output root
                    relativeDir = string.Empty;
                    targetFileName = cabFileKey;
                }

                // Both relativeDir (resolved from the MSI's own Directory table) and
                // targetFileName (the MSI's own File table / raw cabinet member name) are
                // attacker-controlled when the MSI is untrusted. Neither is sanitized upstream,
                // so a crafted "..\..\" directory name or file name could otherwise write outside
                // outputDir (zip-slip / path traversal, OWASP A03). Fail loud on the first escape
                // attempt — a hostile MSI is rejected wholesale rather than partially extracted.
                var resolveResult = ResolveExtractionTarget(outputDir, relativeDir, targetFileName);
                if (resolveResult.IsFailure)
                    return Result<int>.Failure(resolveResult.Error);

                var targetPath = resolveResult.Value;

                remainingBudget -= fileData.LongLength;

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);
                File.WriteAllBytes(targetPath, fileData);
                totalExtracted++;
            }
        }

        return totalExtracted;
    }

    /// <summary>
    /// Resolves the on-disk write target for a single extracted file, given its resolved
    /// directory path and file name (both potentially attacker-controlled — see the write loop
    /// in <see cref="Extract"/>), and verifies the result stays strictly inside
    /// <paramref name="outputDir"/>. Internal (not private) so tests can exercise the containment
    /// check directly with fabricated Directory/File table mappings, without needing a real
    /// malicious MSI.
    /// </summary>
    /// <returns>The full target file path on success, or a <see cref="ErrorKind.SecurityError"/>
    /// failure naming the offending entry when it would escape <paramref name="outputDir"/>.</returns>
    internal static Result<string> ResolveExtractionTarget(string outputDir, string relativeDir, string fileName)
    {
        var relativeKey = Path.Combine(relativeDir, fileName);

        if (!ContainedPathResolver.TryResolveContained(outputDir, relativeKey, out var targetPath))
        {
            return Result<string>.Failure(ErrorKind.SecurityError,
                $"Extraction entry '{relativeKey}' would escape the output directory '{outputDir}' — " +
                "rejecting untrusted MSI (possible path traversal / zip-slip attack).");
        }

        return targetPath;
    }

    /// <summary>
    /// Parses the MSI FileName column format "short|long" and returns the long name,
    /// or the whole string if no pipe separator is present.
    ///
    /// <para>Internal (not private) so <see cref="MsiIntegrityVerifier"/> shares this exact parsing
    /// instead of carrying its own copy — both read the same <c>File.FileName</c> column shape.</para>
    /// </summary>
    internal static string ParseLongFileName(string fileNameField)
    {
        var pipeIndex = fileNameField.IndexOf('|');
        return pipeIndex >= 0 ? fileNameField[(pipeIndex + 1)..] : fileNameField;
    }
}
