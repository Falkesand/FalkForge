using System.Collections.Immutable;
using FalkForge;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Unit tests for <see cref="PrimaryKeyValidator"/>. The validator runs after
/// all producers have emitted rows and before <see cref="MsiRecipeBuilder"/>
/// hands the recipe back to the caller; it must catch duplicate primary keys
/// at recipe-construction time so the executor never asks <c>msi.dll</c> to
/// insert a duplicate-key row.
/// </summary>
public sealed class PrimaryKeyValidatorTests
{
    [Fact]
    public void Validate_with_empty_tables_succeeds()
    {
        Result<Unit> result = PrimaryKeyValidator.Validate(ImmutableArray<RecipeTable>.Empty);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_with_unique_pks_succeeds()
    {
        RecipeTable table = MakeStringPkTable("Property", "ProductCode", "ProductName");

        Result<Unit> result = PrimaryKeyValidator.Validate(ImmutableArray.Create(table));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_with_duplicate_string_pk_fails_with_descriptive_message()
    {
        RecipeTable table = MakeStringPkTable("Property", "Same", "Same");

        Result<Unit> result = PrimaryKeyValidator.Validate(ImmutableArray.Create(table));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Property", result.Error.Message, System.StringComparison.Ordinal);
        Assert.Contains("Same", result.Error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_with_composite_pk_distinguishes_distinct_combos()
    {
        // Two rows with the same first column but different second column should
        // be considered distinct under a composite primary key.
        RecipeTable table = MakeCompositePkTable(
            ("FeatureA", "Comp1"),
            ("FeatureA", "Comp2"));

        Result<Unit> result = PrimaryKeyValidator.Validate(ImmutableArray.Create(table));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_with_duplicate_composite_pk_fails()
    {
        RecipeTable table = MakeCompositePkTable(
            ("FeatureA", "Comp1"),
            ("FeatureA", "Comp1"));

        Result<Unit> result = PrimaryKeyValidator.Validate(ImmutableArray.Create(table));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("FeatureComponents", result.Error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_with_null_pk_cell_fails()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn { Name = "Property", Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Value", Type = ColumnType.String, Width = 0, Nullable = true, LocalizableKey = false });
        ImmutableArray<RecipeRow> rows = ImmutableArray.Create(
            new RecipeRow { Cells = ImmutableArray.Create<CellValue>(new CellValue.Null(), new CellValue.StringValue("v")) });

        RecipeTable table = new()
        {
            Name = TableId.Create("Property").Value,
            Columns = columns,
            Rows = rows,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            CreateTableSql = "CREATE TABLE `Property`",
            InsertViewSql = "SELECT * FROM `Property`",
        };

        Result<Unit> result = PrimaryKeyValidator.Validate(ImmutableArray.Create(table));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Property", result.Error.Message, System.StringComparison.Ordinal);
    }

    private static RecipeTable MakeStringPkTable(string tableName, params string[] keys)
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn { Name = "Property", Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false },
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
        };
    }

    private static RecipeTable MakeCompositePkTable(params (string Feature, string Component)[] keys)
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn { Name = "Feature_", Type = ColumnType.String, Width = 38, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Component_", Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false });
        ImmutableArray<RecipeRow>.Builder rowBuilder = ImmutableArray.CreateBuilder<RecipeRow>(keys.Length);
        foreach ((string feature, string component) in keys)
        {
            rowBuilder.Add(new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue(feature),
                    new CellValue.StringValue(component)),
            });
        }

        return new RecipeTable
        {
            Name = TableId.Create("FeatureComponents").Value,
            Columns = columns,
            Rows = rowBuilder.ToImmutable(),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0), new ColumnIndex(1)),
            CreateTableSql = "CREATE TABLE `FeatureComponents`",
            InsertViewSql = "SELECT * FROM `FeatureComponents`",
        };
    }
}
