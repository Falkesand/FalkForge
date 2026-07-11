using System.Collections.Immutable;
using System.Data.Common;
using FalkForge.Extensions.Sql.Models;
using FalkForge.Validation;

namespace FalkForge.Extensions.Sql;

/// <summary>
/// Rules-as-data for the SQL extension (SQL001–SQL014).
/// Rules close over the database/script/string lists owned by the extension instance.
/// </summary>
public static class SqlRules
{
    private const int MaxCustomActionDataLength = 32767;

    /// <summary>
    /// Builds the full set of <see cref="ValidationRule"/> instances for one <see cref="SqlExtension"/>.
    /// </summary>
    public static ImmutableArray<ValidationRule> Build(
        Func<IReadOnlyList<SqlDatabaseModel>> getDatabases,
        Func<IReadOnlyList<SqlScriptModel>> getScripts,
        Func<IReadOnlyList<SqlStringModel>> getSqlStrings)
    {
        return
        [
            // Database rules

            new ValidationRule(
                new RuleId("SQL001"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "Database requires Server or ConnectionString",
                "Each SQL database must specify either a Server or a ConnectionString.",
                ctx => getDatabases()
                    .Where(db => !string.IsNullOrWhiteSpace(db.Id)
                                 && string.IsNullOrWhiteSpace(db.Server)
                                 && string.IsNullOrWhiteSpace(db.ConnectionString))
                    .Select(db => new Violation(
                        new RuleId("SQL001"), Severity.Error,
                        ModelPath.Root.Field("SqlDatabase").Field(db.Id),
                        "SQL001: Database requires either Server or ConnectionString."))),

            new ValidationRule(
                new RuleId("SQL004"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "Database name is required",
                "Each SQL database must have a non-empty Database name.",
                ctx => getDatabases()
                    .Where(db => !string.IsNullOrWhiteSpace(db.Id) && string.IsNullOrWhiteSpace(db.Database))
                    .Select(db => new Violation(
                        new RuleId("SQL004"), Severity.Error,
                        ModelPath.Root.Field("SqlDatabase").Field(db.Id),
                        "SQL004: Database name is required."))),

            new ValidationRule(
                new RuleId("SQL006"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "Duplicate database Id",
                "Each SQL database must have a unique Id.",
                ctx =>
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    return getDatabases()
                        .Where(db => !string.IsNullOrWhiteSpace(db.Id) && !seen.Add(db.Id))
                        .Select(db => new Violation(
                            new RuleId("SQL006"), Severity.Error,
                            ModelPath.Root.Field("SqlDatabase").Field(db.Id),
                            $"SQL006: Duplicate database Id '{db.Id}'."));
                }),

            new ValidationRule(
                new RuleId("SQL011"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "Database Id is required",
                "Each SQL database must have a non-empty Id.",
                ctx => getDatabases()
                    .Where(db => string.IsNullOrWhiteSpace(db.Id))
                    .Select(_ => new Violation(
                        new RuleId("SQL011"), Severity.Error,
                        ModelPath.Root.Field("SqlDatabase"),
                        "SQL011: Database Id is required."))),

            // Script rules

            new ValidationRule(
                new RuleId("SQL002"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "Script requires a DatabaseRef",
                "Each SQL script and SqlString must reference a database via DatabaseRef.",
                ctx => getScripts()
                    .Where(s => !string.IsNullOrWhiteSpace(s.Id) && string.IsNullOrWhiteSpace(s.DatabaseRef))
                    .Select(s => new Violation(
                        new RuleId("SQL002"), Severity.Error,
                        ModelPath.Root.Field("SqlScript").Field(s.Id),
                        "SQL002: Script requires a DatabaseRef."))
                    .Concat(getSqlStrings()
                        .Where(s => !string.IsNullOrWhiteSpace(s.Id) && string.IsNullOrWhiteSpace(s.DatabaseRef))
                        .Select(s => new Violation(
                            new RuleId("SQL002"), Severity.Error,
                            ModelPath.Root.Field("SqlString").Field(s.Id),
                            "SQL002: SqlString requires a DatabaseRef.")))),

            new ValidationRule(
                new RuleId("SQL003"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "Script must have SourceFile or SqlContent (not both)",
                "Each SQL script must specify exactly one of SourceFile or SqlContent.",
                ctx => getScripts()
                    .Where(s => !string.IsNullOrWhiteSpace(s.Id))
                    .SelectMany(s => ValidateScriptSource(s))),

            new ValidationRule(
                new RuleId("SQL007"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "Duplicate script Id",
                "Each SQL script must have a unique Id.",
                ctx =>
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    return getScripts()
                        .Where(s => !string.IsNullOrWhiteSpace(s.Id) && !seen.Add(s.Id))
                        .Select(s => new Violation(
                            new RuleId("SQL007"), Severity.Error,
                            ModelPath.Root.Field("SqlScript").Field(s.Id),
                            $"SQL007: Duplicate script Id '{s.Id}'."));
                }),

            new ValidationRule(
                new RuleId("SQL009"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "Script SqlContent exceeds maximum length",
                $"Script SqlContent must not exceed {MaxCustomActionDataLength} characters.",
                ctx => getScripts()
                    .Where(s => !string.IsNullOrWhiteSpace(s.Id)
                                && !string.IsNullOrWhiteSpace(s.SqlContent)
                                && s.SqlContent!.Length > MaxCustomActionDataLength)
                    .Select(s => new Violation(
                        new RuleId("SQL009"), Severity.Error,
                        ModelPath.Root.Field("SqlScript").Field(s.Id),
                        $"SQL009: Script SqlContent exceeds maximum length of {MaxCustomActionDataLength} characters."))),

            new ValidationRule(
                new RuleId("SQL012"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "Script Id is required",
                "Each SQL script must have a non-empty Id.",
                ctx => getScripts()
                    .Where(s => string.IsNullOrWhiteSpace(s.Id))
                    .Select(_ => new Violation(
                        new RuleId("SQL012"), Severity.Error,
                        ModelPath.Root.Field("SqlScript"),
                        "SQL012: Script Id is required."))),

            // SqlString rules

            new ValidationRule(
                new RuleId("SQL005"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "SqlString requires a Sql statement",
                "Each SqlString must have a non-empty Sql statement.",
                ctx => getSqlStrings()
                    .Where(s => !string.IsNullOrWhiteSpace(s.Id) && string.IsNullOrWhiteSpace(s.Sql))
                    .Select(s => new Violation(
                        new RuleId("SQL005"), Severity.Error,
                        ModelPath.Root.Field("SqlString").Field(s.Id),
                        "SQL005: SqlString requires a Sql statement."))),

            new ValidationRule(
                new RuleId("SQL008"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "Duplicate SqlString Id",
                "Each SqlString must have a unique Id.",
                ctx =>
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    return getSqlStrings()
                        .Where(s => !string.IsNullOrWhiteSpace(s.Id) && !seen.Add(s.Id))
                        .Select(s => new Violation(
                            new RuleId("SQL008"), Severity.Error,
                            ModelPath.Root.Field("SqlString").Field(s.Id),
                            $"SQL008: Duplicate SqlString Id '{s.Id}'."));
                }),

            new ValidationRule(
                new RuleId("SQL010"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "SqlString Sql exceeds maximum length",
                $"SqlString Sql must not exceed {MaxCustomActionDataLength} characters.",
                ctx => getSqlStrings()
                    .Where(s => !string.IsNullOrWhiteSpace(s.Id)
                                && !string.IsNullOrWhiteSpace(s.Sql)
                                && s.Sql.Length > MaxCustomActionDataLength)
                    .Select(s => new Violation(
                        new RuleId("SQL010"), Severity.Error,
                        ModelPath.Root.Field("SqlString").Field(s.Id),
                        $"SQL010: SqlString Sql exceeds maximum length of {MaxCustomActionDataLength} characters."))),

            new ValidationRule(
                new RuleId("SQL013"),
                Severity.Error,
                ModelSection.Extension_Sql,
                "SqlString Id is required",
                "Each SqlString must have a non-empty Id.",
                ctx => getSqlStrings()
                    .Where(s => string.IsNullOrWhiteSpace(s.Id))
                    .Select(_ => new Violation(
                        new RuleId("SQL013"), Severity.Error,
                        ModelPath.Root.Field("SqlString"),
                        "SQL013: SqlString Id is required."))),

            // Security rule

            new ValidationRule(
                new RuleId("SQL014"),
                Severity.Warning,
                ModelSection.Extension_Sql,
                "ConnectionString contains a plaintext password",
                "Use Integrated Security or Azure AD authentication instead of plaintext passwords.",
                ctx => getDatabases()
                    .Where(db => !string.IsNullOrWhiteSpace(db.Id) && HasPlaintextPassword(db.ConnectionString))
                    .Select(db => new Violation(
                        new RuleId("SQL014"), Severity.Warning,
                        ModelPath.Root.Field("SqlDatabase").Field(db.Id),
                        "SQL014: ConnectionString contains a plaintext password. Use Integrated Security=true or Azure AD authentication instead."))),

            new ValidationRule(
                new RuleId("SQL015"),
                Severity.Warning,
                ModelSection.Extension_Sql,
                "Literal SQL password embedded in the MSI",
                "A literal SQL-authentication password is embedded in plaintext in the MSI. Use PasswordProperty with SetSecureProperty, or Windows integrated authentication.",
                ctx => getDatabases()
                    .Where(db => !string.IsNullOrWhiteSpace(db.Id) && SqlValidator.HasLiteralPassword(db))
                    .Select(db => new Violation(
                        new RuleId("SQL015"), Severity.Warning,
                        ModelPath.Root.Field("SqlDatabase").Field(db.Id),
                        "SQL015: A literal SQL password is embedded in plaintext in the MSI. " +
                        "Use PasswordProperty with SetSecureProperty, or Windows integrated authentication, instead."))),
        ];
    }

    private static IEnumerable<Violation> ValidateScriptSource(SqlScriptModel script)
    {
        var hasSourceFile = !string.IsNullOrWhiteSpace(script.SourceFile);
        var hasSqlContent = !string.IsNullOrWhiteSpace(script.SqlContent);

        if (!hasSourceFile && !hasSqlContent)
            yield return new Violation(
                new RuleId("SQL003"), Severity.Error,
                ModelPath.Root.Field("SqlScript").Field(script.Id),
                "SQL003: Script requires either SourceFile or SqlContent.");

        if (hasSourceFile && hasSqlContent)
            yield return new Violation(
                new RuleId("SQL003"), Severity.Error,
                ModelPath.Root.Field("SqlScript").Field(script.Id),
                "SQL003: Script must specify either SourceFile or SqlContent, not both.");
    }

    private static bool HasPlaintextPassword(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return false;

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            if (builder.TryGetValue("Password", out var pwd) && pwd is string p && p.Length > 0)
                return true;
            if (builder.TryGetValue("Pwd", out var pwd2) && pwd2 is string p2 && p2.Length > 0)
                return true;
        }
        catch (ArgumentException)
        {
            // Malformed connection string — skip check.
        }

        return false;
    }
}
