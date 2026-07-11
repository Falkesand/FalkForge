using System.Data.Common;
using System.Text.RegularExpressions;
using FalkForge.Extensions.Sql.Models;

namespace FalkForge.Extensions.Sql;

public static class SqlValidator
{
    private const int MaxCustomActionDataLength = 32767;

    // Secure/public MSI properties (the only kind that can carry a value into a deferred custom action
    // and be marked secure) are all-uppercase identifiers. Enforcing this steers authors onto a property
    // the engine's SetSecureProperty can actually populate at run time.
    private static readonly Regex PublicPropertyPattern = new(
        "^[A-Z][A-Z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// True when the database model carries a <b>literal</b> SQL password (as opposed to a runtime secure
    /// <see cref="SqlDatabaseModel.PasswordProperty"/> reference or no credentials at all). Backs the
    /// SQL015 compile-time warning: a literal password is embedded in plaintext in the MSI.
    /// </summary>
    public static bool HasLiteralPassword(SqlDatabaseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return !string.IsNullOrEmpty(model.Password);
    }

    /// <summary>
    /// SQL014 — Detects plaintext credentials (Password/Pwd) in a connection string.
    /// Returns failure with SQL014 if a non-empty password is found.
    /// Callers should treat this as a warning: emit a diagnostic but continue compilation.
    /// </summary>
    public static Result<Unit> CheckConnectionStringCredentials(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return Unit.Value;

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

            // DbConnectionStringBuilder normalises key casing; check Password and Pwd variants.
            if (builder.TryGetValue("Password", out var pwd) && pwd is string p && p.Length > 0)
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    "SQL014: ConnectionString contains a plaintext password. " +
                    "Use Integrated Security=true or Azure AD authentication instead.");

            if (builder.TryGetValue("Pwd", out var pwd2) && pwd2 is string p2 && p2.Length > 0)
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    "SQL014: ConnectionString contains a plaintext password (Pwd keyword). " +
                    "Use Integrated Security=true or Azure AD authentication instead.");
        }
        catch (ArgumentException)
        {
            // Malformed connection string — skip credential check; other validation will catch issues.
        }

        return Unit.Value;
    }

    public static Result<Unit> ValidateDatabase(SqlDatabaseModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL011: Database Id is required.");

        if (string.IsNullOrWhiteSpace(model.Database))
            return Result<Unit>.Failure(ErrorKind.Validation, "SQL004: Database name is required.");

        if (string.IsNullOrWhiteSpace(model.Server) && string.IsNullOrWhiteSpace(model.ConnectionString))
            return Result<Unit>.Failure(ErrorKind.Validation,
                "SQL001: Database requires either Server or ConnectionString.");

        var credentials = ValidateCredentials(model);
        if (credentials.IsFailure)
            return credentials;

        return Unit.Value;
    }

    /// <summary>
    /// Validates the SQL-authentication credential shape (SQL016/017/018). A literal
    /// <see cref="SqlDatabaseModel.Password"/> is intentionally NOT an error here — it is allowed but
    /// surfaced as the SQL015 warning (see <see cref="HasLiteralPassword"/>), mirroring REG007/CTB011.
    /// </summary>
    public static Result<Unit> ValidateCredentials(SqlDatabaseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        bool hasProperty = !string.IsNullOrEmpty(model.PasswordProperty);
        bool hasLiteral = !string.IsNullOrEmpty(model.Password);

        if (hasProperty && hasLiteral)
            return Result<Unit>.Failure(ErrorKind.Validation,
                "SQL016: Specify either PasswordProperty (secure, recommended) or Password (literal), not both.");

        if ((hasProperty || hasLiteral) && string.IsNullOrWhiteSpace(model.User))
            return Result<Unit>.Failure(ErrorKind.Validation,
                "SQL017: SQL authentication requires a User when a password is supplied. " +
                "Omit the password for Windows integrated authentication.");

        if (hasProperty && !PublicPropertyPattern.IsMatch(model.PasswordProperty!))
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"SQL018: PasswordProperty '{model.PasswordProperty}' must be a public MSI property " +
                "(uppercase letters, digits and underscore, starting with a letter) so it can be supplied " +
                "securely at run time.");

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
            return Result<Unit>.Failure(ErrorKind.Validation,
                "SQL003: Script requires either SourceFile or SqlContent.");

        if (hasSourceFile && hasSqlContent)
            return Result<Unit>.Failure(ErrorKind.Validation,
                "SQL003: Script must specify either SourceFile or SqlContent, not both.");

        if (hasSqlContent && model.SqlContent!.Length > MaxCustomActionDataLength)
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"SQL009: Script SqlContent exceeds maximum length of {MaxCustomActionDataLength} characters.");

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
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"SQL010: SqlString Sql exceeds maximum length of {MaxCustomActionDataLength} characters.");

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