namespace FalkForge.Decompiler;

/// <summary>
/// The output produced by <see cref="MigrationProjectGenerator"/>: a set of
/// text files (relative path → content) that together form a compilable
/// FalkForge installer project, plus any WiX-specific features that could not
/// be automatically mapped.
/// </summary>
/// <param name="TextFiles">
/// Dictionary keyed by relative file path (e.g. <c>"Program.cs"</c>,
/// <c>"MyProject.csproj"</c>, <c>"MIGRATION-REPORT.md"</c>) with file
/// content as the value.  Callers write these files to the target directory.
/// </param>
/// <param name="Unmapped">
/// WiX Burn features that have no FalkForge equivalent and therefore could not
/// be migrated automatically.  Empty for MSI sources; may be non-empty for
/// WiX bundle sources (handled by a later slice).
/// </param>
/// <param name="Payloads">
/// Payload file bytes extracted from the source installer, keyed by the same
/// relative <c>payload/...</c> path that the generated <c>Program.cs</c> passes to
/// <c>files.Add(...)</c> (see <see cref="PayloadPath"/>).  Callers write each value
/// to its key path under the target directory so the migrated project can repackage
/// the original files.  Empty when payloads cannot be extracted (e.g. the
/// mock-decompiler test path, which has no cabinet access).
/// </param>
public sealed record MigrationResult(
    IReadOnlyDictionary<string, string> TextFiles,
    IReadOnlyList<WixUnmappedFeature> Unmapped,
    IReadOnlyDictionary<string, byte[]> Payloads);
