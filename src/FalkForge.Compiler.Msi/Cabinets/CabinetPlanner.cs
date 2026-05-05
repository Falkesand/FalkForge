using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Cabinets;

/// <summary>
///     Describes a single cabinet to emit into the compiled MSI. The planner
///     is the single source of truth for cabinet splitting so that the
///     Media table rows and the actual _Streams entries cannot drift.
/// </summary>
public sealed record CabinetPlan
{
    public required int DiskId { get; init; }

    /// <summary>Inclusive index of the first file in resolved.Files that belongs to this cabinet.</summary>
    public required int FileStartIndex { get; init; }

    /// <summary>Exclusive end index of the files that belong to this cabinet.</summary>
    public required int FileEndIndex { get; init; }

    /// <summary>Value for Media.LastSequence (= 1-based index of the last File.Sequence this cabinet contains).</summary>
    public required int LastSequence { get; init; }

    /// <summary>File name as it will appear in Media.Cabinet and _Streams.Name (no leading '#').</summary>
    public required string CabinetFileName { get; init; }

    /// <summary>True when the cabinet will be stored inside the MSI as an _Streams entry (Media.Cabinet prefixed '#').</summary>
    public required bool Embedded { get; init; }
}

public static class CabinetPlanner
{
    /// <summary>
    /// Default cabinet file name used when no <see cref="MediaTemplateModel"/> is
    /// configured. Matches <see cref="FalkForge.Compiler.Msi.CabinetBuilder.DefaultCabinetFileName"/>
    /// and is duplicated here so the cross-platform planner has no dependency on
    /// the Windows-only cabinet builder type.
    /// </summary>
    public const string DefaultCabinetFileName = "Data.cab";

    /// <summary>
    ///     Produce a deterministic cabinet split for the given resolved payload.
    ///     When <paramref name="template" /> is null a single <c>Data.cab</c> cabinet
    ///     is planned. With a template the files are split sequentially into
    ///     cabinets whose total size stays at or below the template's maximum
    ///     size; every cabinet carries at least one file.
    /// </summary>
    public static IReadOnlyList<CabinetPlan> Plan(
        IReadOnlyList<ResolvedFile> files,
        MediaTemplateModel? template)
    {
        if (files.Count == 0) return [];

        if (template is null)
            return
            [
                new CabinetPlan
                {
                    DiskId = 1,
                    FileStartIndex = 0,
                    FileEndIndex = files.Count,
                    LastSequence = files.Count,
                    CabinetFileName = DefaultCabinetFileName,
                    Embedded = true
                }
            ];

        var maxCabSizeBytes = template.MaximumCabinetSizeInMB > 0
            ? (long)template.MaximumCabinetSizeInMB * 1024 * 1024
            : 0;

        var plans = new List<CabinetPlan>();
        var diskId = 1;
        var cursor = 0;

        while (cursor < files.Count)
        {
            var startIndex = cursor;
            long running = 0;

            while (cursor < files.Count)
            {
                var fileSize = files[cursor].FileSize;
                // Always include the first file in a cabinet to guarantee forward progress
                // even when a single file exceeds the configured maximum. Split after the
                // current file would push the running total past the cap.
                if (maxCabSizeBytes > 0 && cursor > startIndex && running + fileSize > maxCabSizeBytes)
                    break;

                running += fileSize;
                cursor++;
            }

            plans.Add(new CabinetPlan
            {
                DiskId = diskId,
                FileStartIndex = startIndex,
                FileEndIndex = cursor,
                // Media.LastSequence is the 1-based File.Sequence of the last file
                // in this cabinet. TableEmitter.EmitFiles assigns File.Sequence in
                // iteration order starting at 1, so the 1-based sequence number of
                // the file at the 0-based index (cursor - 1) is exactly `cursor`.
                // The equivalence relies on that stable 1:1 mapping being preserved.
                LastSequence = cursor,
                CabinetFileName = string.Format(template.CabinetTemplate, diskId),
                Embedded = template.EmbedCabinet
            });

            diskId++;
        }

        return plans;
    }
}
