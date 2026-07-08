using FalkForge.Diagnostics;
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
    /// <param name="logger">
    /// Optional structured logger. Defaults to <see langword="null"/> (no-op) so every
    /// existing caller (including the per-schema unit tests that call this directly)
    /// behaves unchanged. When supplied, a <c>Debug</c> entry is logged for the read row
    /// count on success and an <c>Error</c> entry (with the schema's <see cref="TableReadSchema{TRow}.DiagnosticCode"/>
    /// as a <c>code</c> property) is logged before every failing return. Returned
    /// <see cref="Result{T}"/> values are unaffected by whether a logger is supplied.
    /// </param>
    /// <param name="category">Log category; defaults to the MSI decompiler's category.</param>
    public static Result<List<TRow>> ReadOne<TRow>(
        TableReadSchema<TRow> schema,
        ITableQuery access,
        IFalkLogger? logger = null,
        string category = "MsiDecompiler")
    {
        var existsResult = access.TableExists(schema.TableName);
        if (existsResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, category,
                $"{schema.DiagnosticCode}: failed to check existence of '{schema.TableName}' table. {existsResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = schema.DiagnosticCode });
            return Result<List<TRow>>.Failure(existsResult.Error);
        }
        if (!existsResult.Value)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug(category, $"Table '{schema.TableName}' not present; 0 row(s).");
            return Result<List<TRow>>.Success([]);
        }

        var columnNames = schema.Columns.Select(c => c.Name).ToArray();
        var rowsResult = access.QueryTable(schema.TableName, columnNames);
        if (rowsResult.IsFailure)
        {
            var message = $"{schema.DiagnosticCode}: Failed to read {schema.TableName} table. {rowsResult.Error.Message}";
            logger?.Log(LogLevel.Error, category, message, new Dictionary<string, string> { ["code"] = schema.DiagnosticCode });
            return Result<List<TRow>>.Failure(ErrorKind.Validation, message);
        }

        var entries = new List<TRow>(rowsResult.Value.Count);
        var expectedCellCount = schema.Columns.Length;

        for (var rowIndex = 0; rowIndex < rowsResult.Value.Count; rowIndex++)
        {
            var rawRow = rowsResult.Value[rowIndex];

            if (rawRow.Length < expectedCellCount)
            {
                var message = $"{schema.DiagnosticCode}: Table '{schema.TableName}' row {rowIndex} has " +
                    $"{rawRow.Length} cells but schema expects {expectedCellCount}.";
                logger?.Log(LogLevel.Error, category, message, new Dictionary<string, string> { ["code"] = schema.DiagnosticCode });
                return Result<List<TRow>>.Failure(ErrorKind.Validation, message);
            }

            var readRow = new ReadRow(rawRow, schema.TableName, rowIndex);

            try
            {
                var mapResult = schema.Map(readRow);
                if (mapResult.IsFailure)
                {
                    logger?.Log(LogLevel.Error, category,
                        $"{schema.DiagnosticCode}: failed to map row {rowIndex} of '{schema.TableName}': {mapResult.Error.Message}",
                        new Dictionary<string, string> { ["code"] = schema.DiagnosticCode });
                    return Result<List<TRow>>.Failure(mapResult.Error);
                }
                entries.Add(mapResult.Value);
            }
            catch (FormatException ex)
            {
                var message = $"{schema.DiagnosticCode}: {ex.Message}";
                logger?.Log(LogLevel.Error, category, message, ex, new Dictionary<string, string> { ["code"] = schema.DiagnosticCode });
                return Result<List<TRow>>.Failure(ErrorKind.Validation, message);
            }
        }

        if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
            logger.Debug(category, $"Read {entries.Count} row(s) from '{schema.TableName}' table.");

        return entries;
    }
}
