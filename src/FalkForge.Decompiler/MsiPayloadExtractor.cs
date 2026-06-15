using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;

namespace FalkForge.Decompiler;

/// <summary>
/// Extracts payload file bytes from an MSI database, keyed by the relative
/// <c>payload/...</c> path produced by <see cref="PayloadPath.For"/>.
///
/// <para>
/// The keys are computed via <see cref="DirectoryResolver.FindRootFolder"/> — the
/// identical root-excluded segment logic used by
/// <see cref="Recipe.MsiPackageReconstructor"/> when it builds
/// <see cref="FalkForge.Models.FileEntryModel.TargetDirectory"/>. As a result the
/// extracted byte keys align by construction with the <c>files.Add("...")</c> calls
/// emitted by <see cref="CSharpEmitter"/>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class MsiPayloadExtractor
{
    /// <summary>
    /// Opens the MSI at <paramref name="msiPath"/> and returns its payload bytes
    /// keyed by relative payload path. Cabinets that cannot be read are skipped;
    /// unresolved cab entries are omitted (they have no install location to key on).
    /// </summary>
    public static Result<IReadOnlyDictionary<string, byte[]>> Extract(string msiPath)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<IReadOnlyDictionary<string, byte[]>>.Failure(dbResult.Error);

        using var db = dbResult.Value;

        var dirResult = db.QueryRows(
            "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`", 3);
        if (dirResult.IsFailure)
            return Result<IReadOnlyDictionary<string, byte[]>>.Failure(dirResult.Error);

        var dirEntries = dirResult.Value.Select(r => new DirectoryEntry
        {
            DirectoryId = r[0] ?? string.Empty,
            ParentDirectoryId = string.IsNullOrEmpty(r[1]) ? null : r[1],
            DefaultDir = r[2] ?? "."
        }).ToList();

        var resolver = new DirectoryResolver(dirEntries);

        var fileResult = db.QueryRows(
            "SELECT `File`, `Component_`, `FileName`, `Sequence` FROM `File`", 4);
        if (fileResult.IsFailure)
            return Result<IReadOnlyDictionary<string, byte[]>>.Failure(fileResult.Error);

        var compResult = db.QueryRows(
            "SELECT `Component`, `Directory_` FROM `Component`", 2);
        if (compResult.IsFailure)
            return Result<IReadOnlyDictionary<string, byte[]>>.Failure(compResult.Error);

        var componentDirMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in compResult.Value)
        {
            var compId = row[0];
            var dirId = row[1];
            if (compId is not null && dirId is not null)
                componentDirMap[compId] = dirId;
        }

        // Map: cabinet file key → UNIQUE relative payload path. The shared Deduplicator runs
        // over the File rows in table order, identically to CSharpEmitter.EmitFiles, so two
        // files installing to the same directory under the same name each get a distinct key
        // on BOTH sides (otherwise the second file's bytes would overwrite the first's — silent
        // data loss). Single source of truth: PayloadPath.Deduplicator.
        var dedup = new PayloadPath.Deduplicator();
        var fileKeyToPayloadKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in fileResult.Value)
        {
            var fileKey = row[0];
            var componentId = row[1];
            var fileNameField = row[2];

            if (fileKey is null || componentId is null || fileNameField is null)
                continue;

            if (!componentDirMap.TryGetValue(componentId, out var dirId))
                continue;

            var (_, relativePath) = resolver.FindRootFolder(dirId);
            var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var longName = ParseLongFileName(fileNameField);
            fileKeyToPayloadKey[fileKey] = dedup.Next(segments, longName);
        }

        var mediaResult = db.QueryRows(
            "SELECT `DiskId`, `LastSequence`, `Cabinet` FROM `Media`", 3);
        if (mediaResult.IsFailure)
            return Result<IReadOnlyDictionary<string, byte[]>>.Failure(mediaResult.Error);

        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // Cumulative uncompressed-byte budget shared across every embedded cabinet, so a
        // multi-cab decompression bomb cannot bypass the cap by spreading bytes over cabs.
        var remainingBudget = MsiStreamName.MaxTotalUncompressedCabinetBytes;

        foreach (var mediaRow in mediaResult.Value)
        {
            var cabinetName = mediaRow[2];
            if (string.IsNullOrEmpty(cabinetName) || !cabinetName.StartsWith('#'))
                continue; // Only embedded cabinets are supported here.

            var streamName = cabinetName[1..];

            // The cabinet stream name comes from the attacker-controlled Media.Cabinet column.
            // It is interpolated into an MSI-SQL query, so an embedded quote would inject SQL.
            // Allowlist-validate against the legal MSI stream-name shape (<=62 chars, no quotes
            // or other metacharacters) and skip anything that does not match — defense in depth
            // for a malicious MSI (A03: Injection).
            if (!IsValidStreamName(streamName))
                continue;

            var streamResult = db.ReadStream(
                $"SELECT `Name`, `Data` FROM `_Streams` WHERE `Name` = '{streamName}'",
                2, 2);
            if (streamResult.IsFailure)
                continue; // Cabinet may not exist; skip gracefully.

            using var cabStream = new MemoryStream(streamResult.Value);
            var extractResult = CabinetExtractor.Extract(cabStream, remainingBudget);
            if (extractResult.IsFailure)
                return Result<IReadOnlyDictionary<string, byte[]>>.Failure(extractResult.Error);

            foreach (var (cabFileKey, fileData) in extractResult.Value)
            {
                remainingBudget -= fileData.LongLength;
                if (fileKeyToPayloadKey.TryGetValue(cabFileKey, out var payloadKey))
                    payloads[payloadKey] = fileData;
            }
        }

        return Result<IReadOnlyDictionary<string, byte[]>>.Success(payloads);
    }

    /// <summary>
    /// Validates an MSI cabinet stream name against the shared allowlist before it is
    /// interpolated into an MSI-SQL <c>WHERE</c> clause. Thin wrapper over
    /// <see cref="MsiStreamName.IsValid"/> retained for the existing direct unit tests;
    /// the allowlist logic itself lives in one place (<see cref="MsiStreamName"/>).
    /// </summary>
    internal static bool IsValidStreamName(string streamName) => MsiStreamName.IsValid(streamName);

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
