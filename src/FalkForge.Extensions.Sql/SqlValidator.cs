namespace FalkForge.Extensions.Sql;

using FalkForge.Extensions.Sql.Models;

public static class SqlValidator
{
    private const int MaxCustomActionDataLength = 32767;

    public static Result<Unit> ValidateDatabase(SqlDatabaseModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL011: Database Id is required.");

        if (string.IsNullOrWhiteSpace(model.Database))
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL004: Database name is required.");

        if (string.IsNullOrWhiteSpace(model.Server) && string.IsNullOrWhiteSpace(model.ConnectionString))
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL001: Database requires either Server or ConnectionString.");

        return Unit.Value;
    }

    public static Result<Unit> ValidateScript(SqlScriptModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL012: Script Id is required.");

        if (string.IsNullOrWhiteSpace(model.DatabaseRef))
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL002: Script requires a DatabaseRef.");

        var hasSourceFile = !string.IsNullOrWhiteSpace(model.SourceFile);
        var hasSqlContent = !string.IsNullOrWhiteSpace(model.SqlContent);

        if (!hasSourceFile && !hasSqlContent)
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL003: Script requires either SourceFile or SqlContent.");

        if (hasSourceFile && hasSqlContent)
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL003: Script must specify either SourceFile or SqlContent, not both.");

        if (hasSqlContent && model.SqlContent!.Length > MaxCustomActionDataLength)
            return Result<Unit>.Failure(ErrorKind.Validation, $"SQL009: Script SqlContent exceeds maximum length of {MaxCustomActionDataLength} characters.");

        return Unit.Value;
    }

    public static Result<Unit> ValidateString(SqlStringModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL013: SqlString Id is required.");

        if (string.IsNullOrWhiteSpace(model.DatabaseRef))
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL002: SqlString requires a DatabaseRef.");

        if (string.IsNullOrWhiteSpace(model.Sql))
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL005: SqlString requires a Sql statement.");

        if (model.Sql.Length > MaxCustomActionDataLength)
            return Result<Unit>.Failure(ErrorKind.Validation, $"SQL010: SqlString Sql exceeds maximum length of {MaxCustomActionDataLength} characters.");

        return Unit.Value;
    }

    public static Result<Unit> ValidateAll(
        IReadOnlyList<SqlDatabaseModel> databases,
        IReadOnlyList<SqlScriptModel> scripts,
        IReadOnlyList<SqlStringModel> strings)
    {
        var databaseIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var db in databases)
        {
            if (!string.IsNullOrWhiteSpace(db.Id) && !databaseIds.Add(db.Id))
                return Result<Unit>.Failure(ErrorKind.Validation, $"SQL006: Duplicate database Id '{db.Id}'.");

            var result = ValidateDatabase(db);
            if (result.IsFailure)
                return result;
        }

        var scriptIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var script in scripts)
        {
            if (!string.IsNullOrWhiteSpace(script.Id) && !scriptIds.Add(script.Id))
                return Result<Unit>.Failure(ErrorKind.Validation, $"SQL007: Duplicate script Id '{script.Id}'.");

            var result = ValidateScript(script);
            if (result.IsFailure)
                return result;
        }

        var stringIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sqlString in strings)
        {
            if (!string.IsNullOrWhiteSpace(sqlString.Id) && !stringIds.Add(sqlString.Id))
                return Result<Unit>.Failure(ErrorKind.Validation, $"SQL008: Duplicate SqlString Id '{sqlString.Id}'.");

            var result = ValidateString(sqlString);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }
}
