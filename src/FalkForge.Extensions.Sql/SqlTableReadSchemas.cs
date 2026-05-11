using FalkForge.Extensibility;

namespace FalkForge.Extensions.Sql;

/// <summary>
/// Read-side schemas for the MSI <c>SqlDatabase</c>, <c>SqlScript</c>, and
/// <c>SqlString</c> tables. Each schema implements <see cref="ITableReadSchema"/>
/// using only <see cref="ITableQuery"/> from Extensibility, avoiding any reference
/// to the Decompiler assembly.
/// </summary>
internal static class SqlTableReadSchemas
{
    internal static readonly SqlDatabaseReadSchema Database = new();
    internal static readonly SqlScriptReadSchema   Script   = new();
    internal static readonly SqlStringReadSchema   String   = new();
}

// ── SqlDatabase ──────────────────────────────────────────────────────────────

internal sealed class SqlDatabaseReadSchema : ITableReadSchema
{
    private static readonly string[] Columns =
        ["Id", "Server", "Database", "Instance", "ConnectionString",
         "CreateOnInstall", "DropOnUninstall", "ConfirmOverwrite", "Component_"];

    public string TableName => "SqlDatabase";

    public Result<IReadOnlyList<object>> ReadErased(ITableQuery query)
    {
        var existsResult = query.TableExists(TableName);
        if (existsResult.IsFailure)  return Result<IReadOnlyList<object>>.Failure(existsResult.Error);
        if (!existsResult.Value)     return Result<IReadOnlyList<object>>.Success([]);

        var rowsResult = query.QueryTable(TableName, Columns);
        if (rowsResult.IsFailure)
            return Result<IReadOnlyList<object>>.Failure(ErrorKind.Validation,
                $"DEC003: Failed to read SqlDatabase table. {rowsResult.Error.Message}");

        const int expected = 9;
        var result = new List<object>(rowsResult.Value.Count);
        for (var i = 0; i < rowsResult.Value.Count; i++)
        {
            var c = rowsResult.Value[i];
            if (c.Length < expected)
                return Result<IReadOnlyList<object>>.Failure(ErrorKind.Validation,
                    $"DEC003: SqlDatabase row {i} has {c.Length} cells; expected {expected}.");

            result.Add(new SqlDatabaseRow(
                Id:              c[0] ?? string.Empty,
                Server:          c[1],
                Database:        c[2] ?? string.Empty,
                Instance:        c[3],
                ConnectionString: c[4],
                CreateOnInstall: c[5] == "1",
                DropOnUninstall: c[6] == "1",
                ConfirmOverwrite: c[7] == "1",
                Component_:      c[8]));
        }
        return Result<IReadOnlyList<object>>.Success(result);
    }
}

// ── SqlScript ────────────────────────────────────────────────────────────────

internal sealed class SqlScriptReadSchema : ITableReadSchema
{
    private static readonly string[] Columns =
        ["Id", "Database_", "SourceFile", "SqlContent",
         "ExecuteOnInstall", "ExecuteOnReinstall", "ExecuteOnUninstall",
         "RollbackSourceFile", "Sequence", "ContinueOnError", "Component_"];

    public string TableName => "SqlScript";

    public Result<IReadOnlyList<object>> ReadErased(ITableQuery query)
    {
        var existsResult = query.TableExists(TableName);
        if (existsResult.IsFailure)  return Result<IReadOnlyList<object>>.Failure(existsResult.Error);
        if (!existsResult.Value)     return Result<IReadOnlyList<object>>.Success([]);

        var rowsResult = query.QueryTable(TableName, Columns);
        if (rowsResult.IsFailure)
            return Result<IReadOnlyList<object>>.Failure(ErrorKind.Validation,
                $"DEC003: Failed to read SqlScript table. {rowsResult.Error.Message}");

        const int expected = 11;
        var result = new List<object>(rowsResult.Value.Count);
        for (var i = 0; i < rowsResult.Value.Count; i++)
        {
            var c = rowsResult.Value[i];
            if (c.Length < expected)
                return Result<IReadOnlyList<object>>.Failure(ErrorKind.Validation,
                    $"DEC003: SqlScript row {i} has {c.Length} cells; expected {expected}.");

            if (!int.TryParse(c[8], out var sequence))
                return Result<IReadOnlyList<object>>.Failure(ErrorKind.Validation,
                    $"DEC003: SqlScript row {i} Sequence '{c[8]}' is not a valid integer.");

            result.Add(new SqlScriptRow(
                Id:                 c[0] ?? string.Empty,
                Database_:          c[1] ?? string.Empty,
                SourceFile:         c[2],
                SqlContent:         c[3],
                ExecuteOnInstall:   c[4] == "1",
                ExecuteOnReinstall: c[5] == "1",
                ExecuteOnUninstall: c[6] == "1",
                RollbackSourceFile: c[7],
                Sequence:           sequence,
                ContinueOnError:    c[9] == "1",
                Component_:         c[10]));
        }
        return Result<IReadOnlyList<object>>.Success(result);
    }
}

// ── SqlString ────────────────────────────────────────────────────────────────

internal sealed class SqlStringReadSchema : ITableReadSchema
{
    private static readonly string[] Columns =
        ["Id", "Database_", "Sql",
         "ExecuteOnInstall", "ExecuteOnUninstall", "Sequence", "ContinueOnError"];

    public string TableName => "SqlString";

    public Result<IReadOnlyList<object>> ReadErased(ITableQuery query)
    {
        var existsResult = query.TableExists(TableName);
        if (existsResult.IsFailure)  return Result<IReadOnlyList<object>>.Failure(existsResult.Error);
        if (!existsResult.Value)     return Result<IReadOnlyList<object>>.Success([]);

        var rowsResult = query.QueryTable(TableName, Columns);
        if (rowsResult.IsFailure)
            return Result<IReadOnlyList<object>>.Failure(ErrorKind.Validation,
                $"DEC003: Failed to read SqlString table. {rowsResult.Error.Message}");

        const int expected = 7;
        var result = new List<object>(rowsResult.Value.Count);
        for (var i = 0; i < rowsResult.Value.Count; i++)
        {
            var c = rowsResult.Value[i];
            if (c.Length < expected)
                return Result<IReadOnlyList<object>>.Failure(ErrorKind.Validation,
                    $"DEC003: SqlString row {i} has {c.Length} cells; expected {expected}.");

            if (!int.TryParse(c[5], out var sequence))
                return Result<IReadOnlyList<object>>.Failure(ErrorKind.Validation,
                    $"DEC003: SqlString row {i} Sequence '{c[5]}' is not a valid integer.");

            result.Add(new SqlStringRow(
                Id:               c[0] ?? string.Empty,
                Database_:        c[1] ?? string.Empty,
                Sql:              c[2] ?? string.Empty,
                ExecuteOnInstall:  c[3] == "1",
                ExecuteOnUninstall: c[4] == "1",
                Sequence:         sequence,
                ContinueOnError:  c[6] == "1"));
        }
        return Result<IReadOnlyList<object>>.Success(result);
    }
}
