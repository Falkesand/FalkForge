namespace FalkForge.Engine.Msi;

using System.Globalization;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Protocol;

/// <summary>
/// Reads the <c>Feature</c> table of a compiled MSI (read-only) so the UI can offer a
/// per-package feature picker. Non-mutating: opens the database read-only and enumerates
/// features in table order, threading the parent foreign key so callers can rebuild the
/// feature tree.
/// </summary>
/// <remarks>
/// Windows-only: relies on <c>msi.dll</c> via <see cref="MsiDatabase"/>. The estimated
/// install size per feature is deferred (returned as 0) — computing it needs a
/// FeatureComponents/File join that no current caller consumes.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class MsiFeatureReader
{
    /// <summary>
    /// Opens <paramref name="msiPath"/> read-only and returns its <c>Feature</c> rows.
    /// An MSI with no <c>Feature</c> table (or an empty one) yields an empty list.
    /// </summary>
    public static Result<IReadOnlyList<MsiFeatureInfo>> Read(string msiPath)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<IReadOnlyList<MsiFeatureInfo>>.Failure(dbResult.Error);

        using var db = dbResult.Value;

        // Column order: Feature(0), Feature_Parent(1), Title(2), Description(3), Display(4), Level(5).
        var rowsResult = db.QueryRows(
            "SELECT `Feature`, `Feature_Parent`, `Title`, `Description`, `Display`, `Level` FROM `Feature`",
            6);
        if (rowsResult.IsFailure)
            return Result<IReadOnlyList<MsiFeatureInfo>>.Failure(rowsResult.Error);

        var features = new List<MsiFeatureInfo>(rowsResult.Value.Count);
        foreach (var row in rowsResult.Value)
        {
            // Feature is the primary key — a null here means a malformed table row; skip it
            // rather than surfacing a null feature id downstream.
            if (row[0] is not { } featureId)
                continue;

            features.Add(new MsiFeatureInfo(
                FeatureId: featureId,
                Title: row[2],
                Description: row[3],
                Parent: row[1],
                Level: ParseInt(row[5]),
                Display: ParseInt(row[4]),
                EstimatedSize: 0));
        }

        return Result<IReadOnlyList<MsiFeatureInfo>>.Success(features);
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
}
