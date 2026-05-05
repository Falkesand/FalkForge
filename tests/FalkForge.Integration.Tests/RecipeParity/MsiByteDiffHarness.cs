using System.Collections.Immutable;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Models;

namespace FalkForge.Integration.Tests.RecipeParity;

/// <summary>
/// Drives both the legacy <see cref="MsiCompiler"/> and the recipe-driven
/// <see cref="MsiAuthoring"/> facade with the same <see cref="PackageModel"/>
/// and produces a <see cref="MsiDiffReport"/> describing structural and
/// byte-level differences between the two output MSI files.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MsiByteDiffHarness
{
    // Number of bytes of context shown on each side of the first differing byte.
    private const int DiffContextBytes = 8;

    /// <summary>
    /// Compiles <paramref name="package"/> with both the legacy and recipe
    /// pipelines, writes MSI files to isolated temp directories, then compares
    /// the outputs structurally (table sets + row content) and byte-for-byte.
    /// Temp directories are deleted in a <c>finally</c> block.
    /// </summary>
    /// <param name="package">The package model to compile.</param>
    /// <param name="testName">
    /// Short label embedded in the temp directory name for easier diagnosis in
    /// crash dumps or antivirus logs.
    /// </param>
    /// <returns>A <see cref="MsiDiffReport"/> describing all divergences found.</returns>
    public static MsiDiffReport CompareCompilers(PackageModel package, string testName)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(testName);

        // Sanitize testName so it is safe as a directory name fragment.
        var safeLabel = string.Concat(testName.AsSpan(0, Math.Min(testName.Length, 40))
            .ToString()
            .Where(c => char.IsLetterOrDigit(c) || c is '_' or '-'));

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"FalkForge-Phase9-{safeLabel}-{Guid.NewGuid():N}");

        var legacyDir = Path.Combine(tempRoot, "legacy");
        var recipeDir = Path.Combine(tempRoot, "recipe");

        try
        {
            Directory.CreateDirectory(legacyDir);
            Directory.CreateDirectory(recipeDir);

            // --- Legacy compiler ---
            var legacyResult = new MsiCompiler().Compile(package, legacyDir);
            if (legacyResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Legacy MsiCompiler failed: {legacyResult.Error.Message}");
            }

            var legacyMsi = legacyResult.Value;

            // --- Recipe compiler ---
            var recipeResult = MsiAuthoring.Compile(package, recipeDir);
            if (recipeResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Recipe MsiAuthoring failed: {recipeResult.Error.Message}");
            }

            var recipeMsi = recipeResult.Value;

            // --- Structural comparison (table sets + row content) ---
            var diffs = ImmutableArray.CreateBuilder<string>(initialCapacity: 16);
            CompareStructure(legacyMsi, recipeMsi, diffs);

            // --- Byte-level comparison ---
            var legacyBytes = File.ReadAllBytes(legacyMsi);
            var recipeBytes = File.ReadAllBytes(recipeMsi);

            var legacyHash = Convert.ToHexString(SHA256.HashData(legacyBytes));
            var recipeHash = Convert.ToHexString(SHA256.HashData(recipeBytes));

            bool equal = legacyHash == recipeHash;
            string? firstDiff = equal ? null : FindFirstByteDiff(legacyBytes, recipeBytes);

            return new MsiDiffReport(equal, diffs.ToImmutable(), firstDiff);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // -------------------------------------------------------------------------
    // Structural comparison
    // -------------------------------------------------------------------------

    private static void CompareStructure(
        string legacyMsi,
        string recipeMsi,
        ImmutableArray<string>.Builder diffs)
    {
        // Open both databases read-only.
        var legacyDbResult = MsiDatabase.Open(legacyMsi, readOnly: true);
        if (legacyDbResult.IsFailure)
        {
            diffs.Add($"[OpenError] Cannot open legacy MSI: {legacyDbResult.Error.Message}");
            return;
        }

        using var legacyDb = legacyDbResult.Value;

        var recipeDbResult = MsiDatabase.Open(recipeMsi, readOnly: true);
        if (recipeDbResult.IsFailure)
        {
            diffs.Add($"[OpenError] Cannot open recipe MSI: {recipeDbResult.Error.Message}");
            return;
        }

        using var recipeDb = recipeDbResult.Value;

        // Enumerate table names from the MSI internal _Tables table.
        var legacyTables = QueryTableNames(legacyDb);
        var recipeTables = QueryTableNames(recipeDb);

        var onlyInLegacy = legacyTables.Except(recipeTables, StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
        var onlyInRecipe = recipeTables.Except(legacyTables, StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();

        foreach (var t in onlyInLegacy)
            diffs.Add($"[TableSet] Table '{t}' present in legacy, missing from recipe");
        foreach (var t in onlyInRecipe)
            diffs.Add($"[TableSet] Table '{t}' present in recipe, missing from legacy");

        // For tables present in both, compare row counts and row content.
        var commonTables = legacyTables
            .Intersect(recipeTables, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var table in commonTables)
        {
            CompareTableRows(legacyDb, recipeDb, table, diffs);
        }
    }

    private static HashSet<string> QueryTableNames(MsiDatabase db)
    {
        // `_Tables` is a hidden MSI system table that lists every user-visible
        // table name in column 1. Field count = 1.
        var result = db.QueryRows("SELECT `Name` FROM `_Tables`", fieldCount: 1);
        if (result.IsFailure)
            return [];

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in result.Value)
        {
            if (row.Length > 0 && row[0] is { } name)
                names.Add(name);
        }

        return names;
    }

    private static void CompareTableRows(
        MsiDatabase legacyDb,
        MsiDatabase recipeDb,
        string table,
        ImmutableArray<string>.Builder diffs)
    {
        // `_Columns` maps table → (Number, Name) so we know the field count
        // without hard-coding schema knowledge.
        uint fieldCount = GetColumnCount(legacyDb, table);
        if (fieldCount == 0)
        {
            // Fall back to recipe count if legacy returned 0 (shouldn't happen
            // for matched tables, but be defensive).
            fieldCount = GetColumnCount(recipeDb, table);
        }

        if (fieldCount == 0)
        {
            // Cannot determine schema — skip row comparison for this table.
            diffs.Add($"[Table:{table}] Cannot determine column count; skipping row comparison");
            return;
        }

        // Build column selector: `SELECT \`col1\`, \`col2\`, ...`
        // For general tables we fall back to SELECT * which works for string columns.
        // MSI SQL does not support SELECT * for binary/stream columns, so we use
        // a column-count-based star only when we can enumerate column names.
        var legacyRowsResult = QueryAllRows(legacyDb, table, fieldCount);
        var recipeRowsResult = QueryAllRows(recipeDb, table, fieldCount);

        if (legacyRowsResult.IsFailure || recipeRowsResult.IsFailure)
        {
            // If we can't query one side, just note it.
            if (legacyRowsResult.IsFailure)
                diffs.Add($"[Table:{table}] Cannot query legacy rows: {legacyRowsResult.Error.Message}");
            if (recipeRowsResult.IsFailure)
                diffs.Add($"[Table:{table}] Cannot query recipe rows: {recipeRowsResult.Error.Message}");
            return;
        }

        var legacyRows = ToSortedRowSet(legacyRowsResult.Value);
        var recipeRows = ToSortedRowSet(recipeRowsResult.Value);

        if (legacyRows.Count != recipeRows.Count)
        {
            diffs.Add(
                $"[Table:{table}] Row count differs: legacy={legacyRows.Count} recipe={recipeRows.Count}");
        }

        // Report rows present in one but not the other (set diff on serialised row).
        var onlyLegacy = legacyRows.Keys.Except(recipeRows.Keys, StringComparer.Ordinal).Take(10).ToList();
        var onlyRecipe = recipeRows.Keys.Except(legacyRows.Keys, StringComparer.Ordinal).Take(10).ToList();

        foreach (var row in onlyLegacy)
            diffs.Add($"[Table:{table}] Row only in legacy: {row}");
        foreach (var row in onlyRecipe)
            diffs.Add($"[Table:{table}] Row only in recipe: {row}");
    }

    /// <summary>Returns the number of columns in <paramref name="table"/> via <c>_Columns</c>.</summary>
    private static uint GetColumnCount(MsiDatabase db, string table)
    {
        // _Columns has columns (Table, Number, Name, Type).
        // We only need MAX(Number) which MSI SQL does not support, so fetch all and take the max.
        var result = db.QueryRows(
            $"SELECT `Number` FROM `_Columns` WHERE `Table` = '{EscapeIdentifier(table)}'",
            fieldCount: 1);

        if (result.IsFailure || result.Value.Count == 0)
            return 0;

        uint max = 0;
        foreach (var row in result.Value)
        {
            if (row[0] is { } s && uint.TryParse(s, out var n) && n > max)
                max = n;
        }

        return max;
    }

    private static Result<List<string?[]>> QueryAllRows(MsiDatabase db, string table, uint fieldCount)
    {
        // SELECT * on tables with binary stream columns will surface empty strings
        // for those columns, which is acceptable for structural comparison purposes
        // (we are not diffing binary stream content here — that is covered by the
        // byte-level SHA-256 comparison).
        return db.QueryRows($"SELECT * FROM `{EscapeIdentifier(table)}`", fieldCount);
    }

    /// <summary>
    /// Converts a list of rows into a sorted dictionary keyed on a
    /// deterministic string representation of each row for set-diff operations.
    /// Sorting normalises row order divergence between the two pipelines.
    /// </summary>
    private static SortedDictionary<string, string?[]> ToSortedRowSet(List<string?[]> rows)
    {
        var dict = new SortedDictionary<string, string?[]>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = string.Join('\x1F', row.Select(f => f ?? "<null>"));
            // If duplicate rows exist (possible for some MSI tables), append a counter.
            if (!dict.TryAdd(key, row))
            {
                var i = 1;
                while (!dict.TryAdd($"{key}\x1E{i}", row))
                    i++;
            }
        }

        return dict;
    }

    // -------------------------------------------------------------------------
    // Byte-level diff
    // -------------------------------------------------------------------------

    private static string FindFirstByteDiff(byte[] a, byte[] b)
    {
        var minLen = Math.Min(a.Length, b.Length);

        for (var i = 0; i < minLen; i++)
        {
            if (a[i] != b[i])
            {
                var start = Math.Max(0, i - DiffContextBytes);
                var endA = Math.Min(a.Length, i + DiffContextBytes + 1);
                var endB = Math.Min(b.Length, i + DiffContextBytes + 1);
                var ctxA = Convert.ToHexString(a[start..endA]);
                var ctxB = Convert.ToHexString(b[start..endB]);
                return $"Sizes: {a.Length} vs {b.Length}. " +
                       $"First diff at offset 0x{i:X4} ({i}): " +
                       $"legacy=0x{a[i]:X2} recipe=0x{b[i]:X2}. " +
                       $"Context: [{ctxA}] vs [{ctxB}]";
            }
        }

        if (a.Length != b.Length)
        {
            return $"Content identical up to offset {minLen} but sizes differ: " +
                   $"legacy={a.Length} recipe={b.Length}";
        }

        // Should not reach here (caller checks hash equality first).
        return "No byte difference found despite hash mismatch (hash collision?)";
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimal escaping for table/column name identifiers in MSI SQL queries.
    /// MSI identifiers consist of letters, digits, underscores, and dots — no
    /// single-quote characters can legally appear — so this is purely defensive.
    /// </summary>
    private static string EscapeIdentifier(string name)
        => name.Replace("'", "''", StringComparison.Ordinal);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort; OS will reclaim on next boot.
        }
        catch (UnauthorizedAccessException)
        {
            // Same — best-effort only.
        }
    }
}
