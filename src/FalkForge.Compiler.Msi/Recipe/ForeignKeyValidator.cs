using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Recipe-level validator that catches orphan foreign keys before the recipe
/// is handed to the executor. For every cell in a column listed in a table's
/// <see cref="RecipeTable.ForeignKeys"/>, the validator checks that the
/// referenced primary key exists in the target table. Missing target tables
/// are treated as deferred checks and skipped silently — this covers
/// conditionally-emitted tables (e.g. the Icon table, which
/// <see cref="Producers.IconTableProducer"/> suppresses when no icon is
/// authored) whose nullable FK columns then legitimately hold <c>Null</c>.
/// When such a table IS present, its FK cells are fully enforced.
/// </summary>
internal static class ForeignKeyValidator
{
    /// <summary>
    /// Validates every table's foreign-key cells against the per-table primary
    /// key sets built up front. Returns success when all FK cells resolve (or
    /// are <see cref="CellValue.Null"/> in a nullable column). On the first
    /// orphan or null-in-non-nullable-column violation, returns a
    /// <see cref="ErrorKind.Validation"/> failure naming the offending table,
    /// row, column, and the resolved key string.
    /// </summary>
    public static Result<Unit> Validate(IReadOnlyList<RecipeTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        // Build a per-table FrozenSet<string> of primary-key strings. The
        // FrozenSet hot-path is read-only, AOT-safe, and substantially faster
        // than HashSet for repeated lookups during FK validation.
        Dictionary<string, FrozenSet<string>> pkByTable = new(tables.Count, StringComparer.Ordinal);
        for (int t = 0; t < tables.Count; t++)
        {
            RecipeTable table = tables[t];
            HashSet<string> keys = new(table.Rows.Length, StringComparer.Ordinal);
            for (int r = 0; r < table.Rows.Length; r++)
            {
                Result<string> keyResult = PrimaryKeyValidator.ComposeKey(table, table.Rows[r]);
                if (keyResult.IsFailure)
                {
                    // PK validator is expected to have run first and rejected
                    // the recipe before we ever reach FK validation. Surfacing
                    // the same diagnostic here keeps the validator usable in
                    // isolation (e.g., direct calls from tests).
                    return Result<Unit>.Failure(keyResult.Error);
                }

                keys.Add(keyResult.Value);
            }

            pkByTable[table.Name.Value] = keys.ToFrozenSet(StringComparer.Ordinal);
        }

        for (int t = 0; t < tables.Count; t++)
        {
            RecipeTable table = tables[t];
            if (table.ForeignKeys.IsDefaultOrEmpty)
            {
                continue;
            }

            for (int r = 0; r < table.Rows.Length; r++)
            {
                RecipeRow row = table.Rows[r];
                foreach (ForeignKeySpec fk in table.ForeignKeys)
                {
                    Result<Unit> cellResult = ValidateCell(table, row, r, fk, pkByTable);
                    if (cellResult.IsFailure)
                    {
                        return cellResult;
                    }
                }
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static Result<Unit> ValidateCell(
        RecipeTable table,
        RecipeRow row,
        int rowIdx,
        ForeignKeySpec fk,
        Dictionary<string, FrozenSet<string>> pkByTable)
    {
        int colIdx = fk.SourceColumn.Value;
        if (colIdx >= row.Cells.Length)
        {
            return Result<Unit>.Failure(new Error(
                ErrorKind.Validation,
                $"FK source column index {colIdx} out of range for row {rowIdx} in table {table.Name.Value}."));
        }

        // Missing target tables are deferred — see class remarks for the Icon
        // case. This keeps phase-5 leniency explicit so a future phase can
        // tighten it by failing here once all producers exist.
        if (!pkByTable.TryGetValue(fk.TargetTable.Value, out FrozenSet<string>? targetPk))
        {
            return Result<Unit>.Success(Unit.Value);
        }

        CellValue cell = row.Cells[colIdx];
        switch (cell)
        {
            case CellValue.Null:
                if (colIdx < table.Columns.Length && table.Columns[colIdx].Nullable)
                {
                    return Result<Unit>.Success(Unit.Value);
                }

                return Result<Unit>.Failure(new Error(
                    ErrorKind.Validation,
                    $"Null FK in non-nullable column {colIdx} of table {table.Name.Value} row {rowIdx}: target {fk.TargetTable.Value}."));

            case CellValue.ForeignKey fkCell:
                return ResolveAgainstTarget(
                    table, rowIdx, colIdx, fk.TargetTable.Value, fkCell.TargetKey, targetPk);

            case CellValue.StringValue strCell:
                return ResolveAgainstTarget(
                    table, rowIdx, colIdx, fk.TargetTable.Value, strCell.Value, targetPk);

            case CellValue.IntValue intCell:
                string intKey = intCell.Value.ToString(CultureInfo.InvariantCulture);
                return ResolveAgainstTarget(
                    table, rowIdx, colIdx, fk.TargetTable.Value, intKey, targetPk);

            case CellValue.StreamRef:
                return Result<Unit>.Failure(new Error(
                    ErrorKind.Validation,
                    $"Stream cell in FK column {colIdx} of table {table.Name.Value} row {rowIdx}; streams cannot reference a primary key."));

            default:
                return Result<Unit>.Failure(new Error(
                    ErrorKind.Validation,
                    $"Unsupported cell type in FK column {colIdx} of table {table.Name.Value} row {rowIdx}."));
        }
    }

    private static Result<Unit> ResolveAgainstTarget(
        RecipeTable table,
        int rowIdx,
        int colIdx,
        string targetName,
        string targetKey,
        FrozenSet<string> targetPk)
    {
        if (targetPk.Contains(targetKey))
        {
            return Result<Unit>.Success(Unit.Value);
        }

        return Result<Unit>.Failure(new Error(
            ErrorKind.Validation,
            $"Orphan FK in {table.Name.Value} row {rowIdx} column {colIdx}: target table {targetName} key {targetKey}"));
    }
}
