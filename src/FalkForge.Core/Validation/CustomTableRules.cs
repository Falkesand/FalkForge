using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Built-in rules for <see cref="CustomTableModel"/> (CTB001-011).
/// </summary>
public static partial class CustomTableRules
{
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex TableNameRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ColumnNameRegex();

    [GeneratedRegex(@"\[([A-Za-z_][A-Za-z0-9_.]*)\]")]
    private static partial Regex PropertyRefRegex();

    private static readonly FrozenSet<string> SensitiveKeywords =
        FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
            "PASSWORD", "SECRET", "CREDENTIAL", "TOKEN", "APIKEY", "PASSPHRASE", "PIN");

    /// <summary>
    /// Enumerates each sensitive property name referenced in <paramref name="value"/>.
    /// One entry per matching property reference — callers emit one violation per entry.
    /// </summary>
    private static IEnumerable<string> FindSensitiveRefs(string value)
    {
        foreach (Match match in PropertyRefRegex().Matches(value))
        {
            var propName = match.Groups[1].Value;
            var upper = propName.ToUpperInvariant();
            foreach (var kw in SensitiveKeywords)
            {
                if (upper.Contains(kw))
                {
                    yield return propName;
                    break;
                }
            }
        }
    }

    /// <summary>CTB001 — Custom table Name is required.</summary>
    public static readonly ValidationRule Ctb001_NameRequired = new(
        new RuleId("CTB001"),
        Severity.Error,
        ModelSection.CustomTable,
        "Custom table Name required",
        "Every custom table must have a non-empty Name.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(ctx.Package.CustomTables[i].Name))
                    violations.Add(new Violation(new RuleId("CTB001"), Severity.Error,
                        ModelPath.Root.Field("CustomTables").Index(i).Field("Name"),
                        "Custom table Name is required."));
            }
            return violations.ToImmutable();
        });

    /// <summary>CTB002 — Custom table name must not exceed 31 characters.</summary>
    public static readonly ValidationRule Ctb002_NameLength = new(
        new RuleId("CTB002"),
        Severity.Error,
        ModelSection.CustomTable,
        "Custom table name exceeds 31 characters",
        "MSI table names are limited to 31 characters.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                var t = ctx.Package.CustomTables[i];
                if (!string.IsNullOrWhiteSpace(t.Name) && t.Name.Length > 31)
                    violations.Add(new Violation(new RuleId("CTB002"), Severity.Error,
                        ModelPath.Root.Field("CustomTables").Index(i).Field("Name"),
                        $"Custom table '{t.Name}' name exceeds 31 characters."));
            }
            return violations.ToImmutable();
        });

    /// <summary>CTB003 — Custom table name must start with a letter and contain only alphanumeric/underscore.</summary>
    public static readonly ValidationRule Ctb003_NameFormat = new(
        new RuleId("CTB003"),
        Severity.Error,
        ModelSection.CustomTable,
        "Custom table name format invalid",
        "MSI table names must start with a letter and contain only letters, digits, and underscores.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                var t = ctx.Package.CustomTables[i];
                if (!string.IsNullOrWhiteSpace(t.Name) && !TableNameRegex().IsMatch(t.Name))
                    violations.Add(new Violation(new RuleId("CTB003"), Severity.Error,
                        ModelPath.Root.Field("CustomTables").Index(i).Field("Name"),
                        $"Custom table '{t.Name}' name must start with a letter and contain only alphanumeric characters and underscores."));
            }
            return violations.ToImmutable();
        });

    /// <summary>CTB004 — Custom table must have at least one column.</summary>
    public static readonly ValidationRule Ctb004_ColumnsRequired = new(
        new RuleId("CTB004"),
        Severity.Error,
        ModelSection.CustomTable,
        "Custom table requires at least one column",
        "A custom table with no columns is not a valid MSI table.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                var t = ctx.Package.CustomTables[i];
                if (t.Columns.Count == 0)
                    violations.Add(new Violation(new RuleId("CTB004"), Severity.Error,
                        ModelPath.Root.Field("CustomTables").Index(i).Field("Columns"),
                        $"Custom table '{t.Name}' must have at least one column."));
            }
            return violations.ToImmutable();
        });

    /// <summary>CTB005 — Column Name is required.</summary>
    public static readonly ValidationRule Ctb005_ColumnNameRequired = new(
        new RuleId("CTB005"),
        Severity.Error,
        ModelSection.CustomTable,
        "Custom table column Name required",
        "Every column in a custom table must have a non-empty Name.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                var t = ctx.Package.CustomTables[i];
                for (var j = 0; j < t.Columns.Count; j++)
                {
                    if (string.IsNullOrWhiteSpace(t.Columns[j].Name))
                        violations.Add(new Violation(new RuleId("CTB005"), Severity.Error,
                            ModelPath.Root.Field("CustomTables").Index(i).Field("Columns").Index(j).Field("Name"),
                            $"Custom table '{t.Name}' has a column with no name."));
                }
            }
            return violations.ToImmutable();
        });

    /// <summary>CTB006 — Column names must be unique within a table.</summary>
    public static readonly ValidationRule Ctb006_ColumnNameUnique = new(
        new RuleId("CTB006"),
        Severity.Error,
        ModelSection.CustomTable,
        "Custom table column names must be unique",
        "Duplicate column names within a custom table are invalid.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                var t = ctx.Package.CustomTables[i];
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var j = 0; j < t.Columns.Count; j++)
                {
                    var name = t.Columns[j].Name;
                    if (!string.IsNullOrWhiteSpace(name) && !seen.Add(name))
                        violations.Add(new Violation(new RuleId("CTB006"), Severity.Error,
                            ModelPath.Root.Field("CustomTables").Index(i).Field("Columns").Index(j).Field("Name"),
                            $"Custom table '{t.Name}' has duplicate column name '{name}'."));
                }
            }
            return violations.ToImmutable();
        });

    /// <summary>CTB007 — Custom table must have at least one primary key column.</summary>
    public static readonly ValidationRule Ctb007_PrimaryKeyRequired = new(
        new RuleId("CTB007"),
        Severity.Error,
        ModelSection.CustomTable,
        "Custom table primary key required",
        "Every MSI custom table must have at least one primary key column.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                var t = ctx.Package.CustomTables[i];
                if (t.Columns.Count > 0 && !t.Columns.Any(c => c.PrimaryKey))
                    violations.Add(new Violation(new RuleId("CTB007"), Severity.Error,
                        ModelPath.Root.Field("CustomTables").Index(i).Field("Columns"),
                        $"Custom table '{t.Name}' must have at least one primary key column."));
            }
            return violations.ToImmutable();
        });

    /// <summary>CTB008 — Row must not reference an unknown column.</summary>
    public static readonly ValidationRule Ctb008_RowColumnExists = new(
        new RuleId("CTB008"),
        Severity.Error,
        ModelSection.CustomTable,
        "Custom table row references unknown column",
        "Every column name used in a row must match a declared column in the table.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                var t = ctx.Package.CustomTables[i];
                var columnNames = t.Columns
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                    .Select(c => c.Name)
                    .ToFrozenSet(StringComparer.Ordinal);

                for (var r = 0; r < t.Rows.Count; r++)
                {
                    foreach (var col in t.Rows[r].Keys)
                    {
                        if (!columnNames.Contains(col))
                            violations.Add(new Violation(new RuleId("CTB008"), Severity.Error,
                                ModelPath.Root.Field("CustomTables").Index(i).Field("Rows").Index(r).Key(col),
                                $"Custom table '{t.Name}' row references unknown column '{col}'."));
                    }
                }
            }
            return violations.ToImmutable();
        });

    /// <summary>CTB009 — Row value type must match the column's declared type.</summary>
    public static readonly ValidationRule Ctb009_RowValueType = new(
        new RuleId("CTB009"),
        Severity.Error,
        ModelSection.CustomTable,
        "Custom table row value type mismatch",
        "The type of each row value must be compatible with the declared column type.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                var t = ctx.Package.CustomTables[i];
                var colIndex = t.Columns
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                    .ToFrozenDictionary(c => c.Name, StringComparer.Ordinal);

                for (var r = 0; r < t.Rows.Count; r++)
                {
                    foreach (var (colName, value) in t.Rows[r])
                    {
                        if (value is null) continue;
                        if (!colIndex.TryGetValue(colName, out var col)) continue;

                        var isValid = col.Type switch
                        {
                            CustomTableColumnType.String => value is string,
                            CustomTableColumnType.Int16 => value is short or int or long,
                            CustomTableColumnType.Int32 => value is int or long,
                            CustomTableColumnType.Binary => value is string,
                            CustomTableColumnType.Stream => value is string,
                            _ => true
                        };

                        if (!isValid)
                            violations.Add(new Violation(new RuleId("CTB009"), Severity.Error,
                                ModelPath.Root.Field("CustomTables").Index(i).Field("Rows").Index(r).Key(colName),
                                $"Custom table '{t.Name}' column '{colName}' expects type {col.Type} but got {value.GetType().Name}."));
                    }
                }
            }
            return violations.ToImmutable();
        });

    /// <summary>CTB010 — Column name must start with a letter or underscore and contain only alphanumeric/underscore.</summary>
    public static readonly ValidationRule Ctb010_ColumnNameFormat = new(
        new RuleId("CTB010"),
        Severity.Error,
        ModelSection.CustomTable,
        "Custom table column name format invalid",
        "Column names must start with a letter or underscore and contain only letters, digits, and underscores.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                var t = ctx.Package.CustomTables[i];
                for (var j = 0; j < t.Columns.Count; j++)
                {
                    var name = t.Columns[j].Name;
                    if (!string.IsNullOrWhiteSpace(name) && !ColumnNameRegex().IsMatch(name))
                        violations.Add(new Violation(new RuleId("CTB010"), Severity.Error,
                            ModelPath.Root.Field("CustomTables").Index(i).Field("Columns").Index(j).Field("Name"),
                            $"Custom table '{t.Name}' column '{name}' has an invalid name. Column names must start with a letter or underscore and contain only alphanumeric characters and underscores."));
                }
            }
            return violations.ToImmutable();
        });

    /// <summary>CTB011 — Row string value references a sensitive MSI property (warning).</summary>
    public static readonly ValidationRule Ctb011_SensitivePropertyInRow = new(
        new RuleId("CTB011"),
        Severity.Warning,
        ModelSection.CustomTable,
        "Sensitive property in custom table row",
        "Sensitive property values written to custom tables are stored in plaintext inside the MSI.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomTables.Count; i++)
            {
                var t = ctx.Package.CustomTables[i];
                for (var r = 0; r < t.Rows.Count; r++)
                {
                    foreach (var (colName, value) in t.Rows[r])
                    {
                        if (value is not string strVal) continue;
                        // Emit one violation per sensitive property reference (mirrors legacy one-per-property behavior).
                        foreach (var propName in FindSensitiveRefs(strVal))
                            violations.Add(new Violation(new RuleId("CTB011"), Severity.Warning,
                                ModelPath.Root.Field("CustomTables").Index(i).Field("Rows").Index(r).Key(colName),
                                $"Custom table '{t.Name}' column '{colName}' references property '[{propName}]' which appears to contain sensitive data. " +
                                "Sensitive values written to custom tables are stored in plaintext inside the MSI. Consider alternative secure storage."));
                    }
                }
            }
            return violations.ToImmutable();
        });

    /// <summary>All CTB rules in order.</summary>
    public static readonly ValidationRule[] All =
    [
        Ctb001_NameRequired,
        Ctb002_NameLength,
        Ctb003_NameFormat,
        Ctb004_ColumnsRequired,
        Ctb005_ColumnNameRequired,
        Ctb006_ColumnNameUnique,
        Ctb007_PrimaryKeyRequired,
        Ctb008_RowColumnExists,
        Ctb009_RowValueType,
        Ctb010_ColumnNameFormat,
        Ctb011_SensitivePropertyInRow
    ];
}
