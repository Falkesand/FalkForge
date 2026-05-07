using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

/// <summary>
/// Per-rule isolated tests for CustomTableRules (CTB001-011).
/// </summary>
public sealed class CustomTableRulesTests
{
    private static RuleContext Ctx(PackageModel pkg) => RuleContext.ForTest(pkg);

    private static PackageModel Pkg(params CustomTableModel[] tables) => new()
    {
        Name = "App",
        Manufacturer = "Corp",
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid(),
        CustomTables = tables.ToList()
    };

    private static CustomTableColumnModel PkCol(string name = "Id") =>
        new() { Name = name, PrimaryKey = true };

    private static CustomTableModel ValidTable(string name = "MyTable") =>
        new() { Name = name, Columns = [PkCol()] };

    // ── CTB001 — Custom table Name required ──────────────────────────────────

    [Fact]
    public void Ctb001_empty_name_yields_error()
    {
        var pkg = Pkg(new CustomTableModel { Name = "", Columns = [PkCol()] });
        var violations = CustomTableRules.Ctb001_NameRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB001", violations[0].RuleId.Value);
        Assert.Equal(Severity.Error, violations[0].Severity);
    }

    [Fact]
    public void Ctb001_valid_name_yields_no_violations()
    {
        Assert.Empty(CustomTableRules.Ctb001_NameRequired.Evaluate(Ctx(Pkg(ValidTable()))));
    }

    // ── CTB002 — Name length ≤ 31 ────────────────────────────────────────────

    [Fact]
    public void Ctb002_name_over_31_chars_yields_error()
    {
        var pkg = Pkg(new CustomTableModel { Name = new string('A', 32), Columns = [PkCol()] });
        var violations = CustomTableRules.Ctb002_NameLength.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB002", violations[0].RuleId.Value);
    }

    [Fact]
    public void Ctb002_name_exactly_31_chars_yields_no_violations()
    {
        var pkg = Pkg(new CustomTableModel { Name = new string('A', 31), Columns = [PkCol()] });
        Assert.Empty(CustomTableRules.Ctb002_NameLength.Evaluate(Ctx(pkg)));
    }

    // ── CTB003 — Name format ─────────────────────────────────────────────────

    [Fact]
    public void Ctb003_name_starting_with_digit_yields_error()
    {
        var pkg = Pkg(new CustomTableModel { Name = "1BadName", Columns = [PkCol()] });
        var violations = CustomTableRules.Ctb003_NameFormat.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB003", violations[0].RuleId.Value);
    }

    [Fact]
    public void Ctb003_name_with_spaces_yields_error()
    {
        var pkg = Pkg(new CustomTableModel { Name = "My Table", Columns = [PkCol()] });
        Assert.Single(CustomTableRules.Ctb003_NameFormat.Evaluate(Ctx(pkg)).ToList());
    }

    [Fact]
    public void Ctb003_valid_alphanumeric_name_yields_no_violations()
    {
        Assert.Empty(CustomTableRules.Ctb003_NameFormat.Evaluate(Ctx(Pkg(ValidTable()))));
    }

    // ── CTB004 — At least one column required ────────────────────────────────

    [Fact]
    public void Ctb004_no_columns_yields_error()
    {
        var pkg = Pkg(new CustomTableModel { Name = "T1" });
        var violations = CustomTableRules.Ctb004_ColumnsRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB004", violations[0].RuleId.Value);
    }

    [Fact]
    public void Ctb004_with_column_yields_no_violations()
    {
        Assert.Empty(CustomTableRules.Ctb004_ColumnsRequired.Evaluate(Ctx(Pkg(ValidTable()))));
    }

    // ── CTB005 — Column Name required ────────────────────────────────────────

    [Fact]
    public void Ctb005_empty_column_name_yields_error()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [new CustomTableColumnModel { Name = "", PrimaryKey = true }]
        });
        var violations = CustomTableRules.Ctb005_ColumnNameRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB005", violations[0].RuleId.Value);
    }

    // ── CTB006 — Duplicate column names ──────────────────────────────────────

    [Fact]
    public void Ctb006_duplicate_column_name_yields_error()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [PkCol("Id"), new CustomTableColumnModel { Name = "Id" }]
        });
        var violations = CustomTableRules.Ctb006_ColumnNameUnique.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB006", violations[0].RuleId.Value);
    }

    [Fact]
    public void Ctb006_unique_column_names_yield_no_violations()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [PkCol("Id"), new CustomTableColumnModel { Name = "Value" }]
        });
        Assert.Empty(CustomTableRules.Ctb006_ColumnNameUnique.Evaluate(Ctx(pkg)));
    }

    // ── CTB007 — At least one primary key ────────────────────────────────────

    [Fact]
    public void Ctb007_no_primary_key_yields_error()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [new CustomTableColumnModel { Name = "Value" }]
        });
        var violations = CustomTableRules.Ctb007_PrimaryKeyRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB007", violations[0].RuleId.Value);
    }

    [Fact]
    public void Ctb007_with_primary_key_yields_no_violations()
    {
        Assert.Empty(CustomTableRules.Ctb007_PrimaryKeyRequired.Evaluate(Ctx(Pkg(ValidTable()))));
    }

    // ── CTB008 — Row references unknown column ───────────────────────────────

    [Fact]
    public void Ctb008_row_with_unknown_column_yields_error()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [PkCol("Id")],
            Rows = [new Dictionary<string, object?> { ["UnknownCol"] = "val" }]
        });
        var violations = CustomTableRules.Ctb008_RowColumnExists.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB008", violations[0].RuleId.Value);
    }

    [Fact]
    public void Ctb008_row_with_known_column_yields_no_violations()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [PkCol("Id")],
            Rows = [new Dictionary<string, object?> { ["Id"] = "val1" }]
        });
        Assert.Empty(CustomTableRules.Ctb008_RowColumnExists.Evaluate(Ctx(pkg)));
    }

    // ── CTB009 — Row value type mismatch ─────────────────────────────────────

    [Fact]
    public void Ctb009_int_value_in_string_column_yields_error()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [new CustomTableColumnModel { Name = "Name", Type = CustomTableColumnType.String, PrimaryKey = true }],
            Rows = [new Dictionary<string, object?> { ["Name"] = 42 }]
        });
        var violations = CustomTableRules.Ctb009_RowValueType.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB009", violations[0].RuleId.Value);
    }

    [Fact]
    public void Ctb009_string_value_in_string_column_yields_no_violations()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [PkCol("Id")],
            Rows = [new Dictionary<string, object?> { ["Id"] = "val" }]
        });
        Assert.Empty(CustomTableRules.Ctb009_RowValueType.Evaluate(Ctx(pkg)));
    }

    [Fact]
    public void Ctb009_null_value_yields_no_violations()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [PkCol("Id")],
            Rows = [new Dictionary<string, object?> { ["Id"] = null }]
        });
        Assert.Empty(CustomTableRules.Ctb009_RowValueType.Evaluate(Ctx(pkg)));
    }

    // ── CTB010 — Column name format ───────────────────────────────────────────

    [Fact]
    public void Ctb010_column_name_with_dash_yields_error()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [new CustomTableColumnModel { Name = "my-col", PrimaryKey = true }]
        });
        var violations = CustomTableRules.Ctb010_ColumnNameFormat.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB010", violations[0].RuleId.Value);
    }

    [Fact]
    public void Ctb010_valid_column_name_yields_no_violations()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [new CustomTableColumnModel { Name = "_MyColumn123", PrimaryKey = true }]
        });
        Assert.Empty(CustomTableRules.Ctb010_ColumnNameFormat.Evaluate(Ctx(pkg)));
    }

    // ── CTB011 — Sensitive property in custom table value (warning) ───────────

    [Fact]
    public void Ctb011_password_property_in_row_yields_warning()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [PkCol("Key"), new CustomTableColumnModel { Name = "Value" }],
            Rows = [new Dictionary<string, object?> { ["Key"] = "k", ["Value"] = "[MY_PASSWORD]" }]
        });
        var violations = CustomTableRules.Ctb011_SensitivePropertyInRow.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CTB011", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Ctb011_non_sensitive_value_yields_no_violations()
    {
        var pkg = Pkg(new CustomTableModel
        {
            Name = "T1",
            Columns = [PkCol("Key"), new CustomTableColumnModel { Name = "Value" }],
            Rows = [new Dictionary<string, object?> { ["Key"] = "k", ["Value"] = "[INSTALLFOLDER]" }]
        });
        Assert.Empty(CustomTableRules.Ctb011_SensitivePropertyInRow.Evaluate(Ctx(pkg)));
    }
}
