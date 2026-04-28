using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class TableSchemaTests
{
    private static RecipeColumn MakeColumn(string name) => new()
    {
        Name = name,
        Type = ColumnType.String,
        Width = 72,
        Nullable = false,
        LocalizableKey = false,
    };

    private static TableId Id(string name) => TableId.Create(name).Value;

    [Fact]
    public void Construct_two_columns_single_pk_no_fks_succeeds()
    {
        TableSchema schema = new()
        {
            Name = Id("Property"),
            Columns = ImmutableArray.Create(MakeColumn("Property"), MakeColumn("Value")),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };

        Assert.Equal("Property", schema.Name.Value);
        Assert.Equal(2, schema.Columns.Length);
        Assert.Single(schema.PrimaryKey);
        Assert.Empty(schema.ForeignKeys);
    }

    [Fact]
    public void Construct_with_one_foreign_key_succeeds()
    {
        ForeignKeySpec fk = new()
        {
            SourceColumn = new ColumnIndex(1),
            TargetTable = Id("Component"),
        };

        TableSchema schema = new()
        {
            Name = Id("File"),
            Columns = ImmutableArray.Create(MakeColumn("File"), MakeColumn("Component_")),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(fk),
        };

        Assert.Single(schema.ForeignKeys);
        Assert.Equal(new ColumnIndex(1), schema.ForeignKeys[0].SourceColumn);
        Assert.Equal(Id("Component"), schema.ForeignKeys[0].TargetTable);
    }

    [Fact]
    public void Empty_columns_throws_argument_exception()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => new TableSchema
        {
            Name = Id("Empty"),
            Columns = ImmutableArray<RecipeColumn>.Empty,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        });

        Assert.Contains("Columns must contain at least one column", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_primary_key_throws_argument_exception()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => new TableSchema
        {
            Name = Id("Property"),
            Columns = ImmutableArray.Create(MakeColumn("Property"), MakeColumn("Value")),
            PrimaryKey = ImmutableArray<ColumnIndex>.Empty,
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        });

        Assert.Contains("PrimaryKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Primary_key_index_below_zero_is_blocked_by_column_index_constructor()
    {
        // ColumnIndex itself enforces non-negative; verify that path is the wall.
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColumnIndex(-1));
    }

    [Fact]
    public void Primary_key_index_at_or_above_columns_length_throws()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new TableSchema
        {
            Name = Id("Property"),
            Columns = ImmutableArray.Create(MakeColumn("Property"), MakeColumn("Value")),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(2)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        });

        Assert.Contains("PrimaryKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_primary_key_indices_throws_argument_exception()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => new TableSchema
        {
            Name = Id("Property"),
            Columns = ImmutableArray.Create(MakeColumn("Property"), MakeColumn("Value")),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0), new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        });

        Assert.Contains("distinct", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_column_names_throws_argument_exception()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => new TableSchema
        {
            Name = Id("Property"),
            Columns = ImmutableArray.Create(MakeColumn("Property"), MakeColumn("Property")),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        });

        Assert.Contains("unique", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Foreign_key_source_column_out_of_range_throws()
    {
        ForeignKeySpec fk = new()
        {
            SourceColumn = new ColumnIndex(5),
            TargetTable = Id("Component"),
        };

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new TableSchema
        {
            Name = Id("File"),
            Columns = ImmutableArray.Create(MakeColumn("File"), MakeColumn("Component_")),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(fk),
        });

        Assert.Contains("ForeignKeys", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_expression_preserves_immutability()
    {
        TableSchema original = new()
        {
            Name = Id("Property"),
            Columns = ImmutableArray.Create(MakeColumn("Property"), MakeColumn("Value")),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };

        TableSchema renamed = original with { Name = Id("Renamed") };

        Assert.Equal("Property", original.Name.Value);
        Assert.Equal("Renamed", renamed.Name.Value);
        Assert.Equal(original.Columns, renamed.Columns);
        Assert.Equal(original.PrimaryKey, renamed.PrimaryKey);
    }
}
