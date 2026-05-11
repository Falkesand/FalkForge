using FalkForge.Extensibility;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Engine that drives a <see cref="TableReadSchema{TRow}"/> against an
/// <see cref="ITableQuery"/> (or <see cref="IMsiTableAccess"/>) instance.
/// Replaces the per-reader copy-pasted orchestration loop
/// (TableExists + QueryTable + iterate + accumulate).
/// <para>
/// <see cref="ReadOne{TRow}"/> is the primary entry point for isolated per-schema
/// unit tests — give it a <see cref="TableReadSchema{TRow}"/> and an
/// <see cref="ITableQuery"/> and get back a typed list.
/// </para>
/// </summary>
public static class TableReadEngine
{
    /// <summary>
    /// Reads all rows from the table described by <paramref name="schema"/> via
    /// <paramref name="access"/>. Returns an empty list when the table does not
    /// exist. Returns a structured failure when the table query fails or when any
    /// row has a shape or type mismatch.
    /// </summary>
    public static Result<List<TRow>> ReadOne<TRow>(
        TableReadSchema<TRow> schema,
        ITableQuery access)
    {
        var existsResult = access.TableExists(schema.TableName);
        if (existsResult.IsFailure)
            return Result<List<TRow>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<List<TRow>>.Success([]);

        var columnNames = schema.Columns.Select(c => c.Name).ToArray();
        var rowsResult = access.QueryTable(schema.TableName, columnNames);
        if (rowsResult.IsFailure)
            return Result<List<TRow>>.Failure(
                ErrorKind.Validation,
                $"{schema.DiagnosticCode}: Failed to read {schema.TableName} table. {rowsResult.Error.Message}");

        var entries = new List<TRow>(rowsResult.Value.Count);
        var expectedCellCount = schema.Columns.Length;

        for (var rowIndex = 0; rowIndex < rowsResult.Value.Count; rowIndex++)
        {
            var rawRow = rowsResult.Value[rowIndex];

            if (rawRow.Length < expectedCellCount)
            {
                return Result<List<TRow>>.Failure(
                    ErrorKind.Validation,
                    $"{schema.DiagnosticCode}: Table '{schema.TableName}' row {rowIndex} has " +
                    $"{rawRow.Length} cells but schema expects {expectedCellCount}.");
            }

            var readRow = new ReadRow(rawRow, schema.TableName, rowIndex);

            try
            {
                var mapResult = schema.Map(readRow);
                if (mapResult.IsFailure)
                    return Result<List<TRow>>.Failure(mapResult.Error);
                entries.Add(mapResult.Value);
            }
            catch (FormatException ex)
            {
                return Result<List<TRow>>.Failure(
                    ErrorKind.Validation,
                    $"{schema.DiagnosticCode}: {ex.Message}");
            }
        }

        return entries;
    }
}
