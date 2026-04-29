using System.Collections.Immutable;
using FalkForge;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Unit tests for <see cref="ForeignKeyValidator"/>. The validator runs after
/// every producer has emitted its rows — and after
/// <see cref="PrimaryKeyValidator"/> has confirmed each table's PK is unique
/// — to ensure every cell in an FK position references an existing primary
/// key in the target table. Missing target tables are treated as deferred
/// checks (the Icon table is not yet a producer; the FK column may legitimately
/// hold <c>Null</c>) and skipped silently.
/// </summary>
public sealed class ForeignKeyValidatorTests
{
    [Fact]
    public void Validate_with_no_fks_succeeds()
    {
        RecipeTable table = MakeStringPkTable("Property", ImmutableArray<ForeignKeySpec>.Empty, "ProductCode");

        Result<Unit> result = ForeignKeyValidator.Validate(ImmutableArray.Create(table));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_with_resolving_fk_succeeds()
    {
        RecipeTable directory = MakeStringPkTable("Directory", ImmutableArray<ForeignKeySpec>.Empty, "TARGETDIR");
        TableId directoryTable = TableId.Create("Directory").Value;
        ImmutableArray<ForeignKeySpec> componentFks = ImmutableArray.Create(new ForeignKeySpec
        {
            SourceColumn = new ColumnIndex(1),
            TargetTable = directoryTable,
        });
        RecipeTable component = MakeFkTable(
            "Component",
            componentFks,
            ("Comp1", new CellValue.ForeignKey(directoryTable, "TARGETDIR")));

        Result<Unit> result = ForeignKeyValidator.Validate(ImmutableArray.Create(directory, component));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_with_orphan_fk_fails_with_descriptive_message()
    {
        RecipeTable directory = MakeStringPkTable("Directory", ImmutableArray<ForeignKeySpec>.Empty, "TARGETDIR");
        TableId directoryTable = TableId.Create("Directory").Value;
        ImmutableArray<ForeignKeySpec> componentFks = ImmutableArray.Create(new ForeignKeySpec
        {
            SourceColumn = new ColumnIndex(1),
            TargetTable = directoryTable,
        });
        RecipeTable component = MakeFkTable(
            "Component",
            componentFks,
            ("Comp1", new CellValue.ForeignKey(directoryTable, "MISSINGDIR")));

        Result<Unit> result = ForeignKeyValidator.Validate(ImmutableArray.Create(directory, component));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Component", result.Error.Message, System.StringComparison.Ordinal);
        Assert.Contains("Directory", result.Error.Message, System.StringComparison.Ordinal);
        Assert.Contains("MISSINGDIR", result.Error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_with_null_fk_in_nullable_column_succeeds()
    {
        RecipeTable directory = MakeStringPkTable("Directory", ImmutableArray<ForeignKeySpec>.Empty, "TARGETDIR");
        TableId directoryTable = TableId.Create("Directory").Value;
        ImmutableArray<ForeignKeySpec> fks = ImmutableArray.Create(new ForeignKeySpec
        {
            SourceColumn = new ColumnIndex(1),
            TargetTable = directoryTable,
        });
        RecipeTable shortcut = MakeFkTableNullableFkColumn(
            "Shortcut",
            fks,
            ("Sc1", new CellValue.Null()));

        Result<Unit> result = ForeignKeyValidator.Validate(ImmutableArray.Create(directory, shortcut));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_with_null_fk_in_non_nullable_column_fails()
    {
        RecipeTable directory = MakeStringPkTable("Directory", ImmutableArray<ForeignKeySpec>.Empty, "TARGETDIR");
        TableId directoryTable = TableId.Create("Directory").Value;
        ImmutableArray<ForeignKeySpec> fks = ImmutableArray.Create(new ForeignKeySpec
        {
            SourceColumn = new ColumnIndex(1),
            TargetTable = directoryTable,
        });
        RecipeTable component = MakeFkTable(
            "Component",
            fks,
            ("Comp1", new CellValue.Null()));

        Result<Unit> result = ForeignKeyValidator.Validate(ImmutableArray.Create(directory, component));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Component", result.Error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_with_string_value_in_fk_position_resolves_against_target_pk()
    {
        // Tolerance for legacy-shape producer cells: an FK column may carry a
        // CellValue.StringValue rather than a CellValue.ForeignKey. The validator
        // should still resolve it against the declared target table's PK set.
        RecipeTable directory = MakeStringPkTable("Directory", ImmutableArray<ForeignKeySpec>.Empty, "TARGETDIR");
        TableId directoryTable = TableId.Create("Directory").Value;
        ImmutableArray<ForeignKeySpec> fks = ImmutableArray.Create(new ForeignKeySpec
        {
            SourceColumn = new ColumnIndex(1),
            TargetTable = directoryTable,
        });
        RecipeTable component = MakeFkTable(
            "Component",
            fks,
            ("Comp1", new CellValue.StringValue("TARGETDIR")));

        Result<Unit> result = ForeignKeyValidator.Validate(ImmutableArray.Create(directory, component));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_with_missing_target_table_skips_silently()
    {
        // Target table not present in the recipe (e.g., Icon table not yet a
        // producer). FK validation must not error — phase 9 byte-diff catches
        // any real downstream issue.
        TableId iconTable = TableId.Create("Icon").Value;
        ImmutableArray<ForeignKeySpec> fks = ImmutableArray.Create(new ForeignKeySpec
        {
            SourceColumn = new ColumnIndex(1),
            TargetTable = iconTable,
        });
        RecipeTable shortcut = MakeFkTableNullableFkColumn(
            "Shortcut",
            fks,
            ("Sc1", new CellValue.StringValue("SomeIcon")));

        Result<Unit> result = ForeignKeyValidator.Validate(ImmutableArray.Create(shortcut));

        Assert.True(result.IsSuccess);
    }

    private static RecipeTable MakeStringPkTable(
        string tableName,
        ImmutableArray<ForeignKeySpec> foreignKeys,
        params string[] keys)
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn { Name = "Key", Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Value", Type = ColumnType.String, Width = 0, Nullable = true, LocalizableKey = false });
        ImmutableArray<RecipeRow>.Builder rowBuilder = ImmutableArray.CreateBuilder<RecipeRow>(keys.Length);
        foreach (string key in keys)
        {
            rowBuilder.Add(new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(new CellValue.StringValue(key), new CellValue.StringValue("v")),
            });
        }

        return new RecipeTable
        {
            Name = TableId.Create(tableName).Value,
            Columns = columns,
            Rows = rowBuilder.ToImmutable(),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            CreateTableSql = $"CREATE TABLE `{tableName}`",
            InsertViewSql = $"SELECT * FROM `{tableName}`",
            ForeignKeys = foreignKeys,
        };
    }

    private static RecipeTable MakeFkTable(
        string tableName,
        ImmutableArray<ForeignKeySpec> foreignKeys,
        params (string Pk, CellValue Fk)[] rows)
    {
        // Two-column shape: [PK : NOT NULL] + [FK : NOT NULL].
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn { Name = "Id", Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Ref_", Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false });
        ImmutableArray<RecipeRow>.Builder rowBuilder = ImmutableArray.CreateBuilder<RecipeRow>(rows.Length);
        foreach ((string pk, CellValue fk) in rows)
        {
            rowBuilder.Add(new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(new CellValue.StringValue(pk), fk),
            });
        }

        return new RecipeTable
        {
            Name = TableId.Create(tableName).Value,
            Columns = columns,
            Rows = rowBuilder.ToImmutable(),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            CreateTableSql = $"CREATE TABLE `{tableName}`",
            InsertViewSql = $"SELECT * FROM `{tableName}`",
            ForeignKeys = foreignKeys,
        };
    }

    private static RecipeTable MakeFkTableNullableFkColumn(
        string tableName,
        ImmutableArray<ForeignKeySpec> foreignKeys,
        params (string Pk, CellValue Fk)[] rows)
    {
        // Same as MakeFkTable but the FK column is declared Nullable = true.
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn { Name = "Id", Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Ref_", Type = ColumnType.String, Width = 72, Nullable = true, LocalizableKey = false });
        ImmutableArray<RecipeRow>.Builder rowBuilder = ImmutableArray.CreateBuilder<RecipeRow>(rows.Length);
        foreach ((string pk, CellValue fk) in rows)
        {
            rowBuilder.Add(new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(new CellValue.StringValue(pk), fk),
            });
        }

        return new RecipeTable
        {
            Name = TableId.Create(tableName).Value,
            Columns = columns,
            Rows = rowBuilder.ToImmutable(),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            CreateTableSql = $"CREATE TABLE `{tableName}`",
            InsertViewSql = $"SELECT * FROM `{tableName}`",
            ForeignKeys = foreignKeys,
        };
    }
}
