using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class ForeignKeySpecTests
{
    [Fact]
    public void Construct_with_required_members_preserves_values()
    {
        ColumnIndex source = new(2);
        TableId target = TableId.Create("Component").Value;

        ForeignKeySpec spec = new()
        {
            SourceColumn = source,
            TargetTable = target,
        };

        Assert.Equal(source, spec.SourceColumn);
        Assert.Equal(target, spec.TargetTable);
    }

    [Fact]
    public void Equal_specs_compare_equal()
    {
        ColumnIndex source = new(1);
        TableId target = TableId.Create("File").Value;

        ForeignKeySpec a = new() { SourceColumn = source, TargetTable = target };
        ForeignKeySpec b = new() { SourceColumn = source, TargetTable = target };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Specs_with_different_source_column_are_not_equal()
    {
        TableId target = TableId.Create("Component").Value;

        ForeignKeySpec a = new() { SourceColumn = new ColumnIndex(0), TargetTable = target };
        ForeignKeySpec b = new() { SourceColumn = new ColumnIndex(1), TargetTable = target };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Specs_with_different_target_table_are_not_equal()
    {
        ColumnIndex source = new(0);
        TableId targetA = TableId.Create("Component").Value;
        TableId targetB = TableId.Create("File").Value;

        ForeignKeySpec a = new() { SourceColumn = source, TargetTable = targetA };
        ForeignKeySpec b = new() { SourceColumn = source, TargetTable = targetB };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void With_expression_updates_field_and_preserves_others()
    {
        TableId target = TableId.Create("Component").Value;
        ForeignKeySpec original = new()
        {
            SourceColumn = new ColumnIndex(0),
            TargetTable = target,
        };

        ForeignKeySpec updated = original with { SourceColumn = new ColumnIndex(3) };

        Assert.Equal(new ColumnIndex(3), updated.SourceColumn);
        Assert.Equal(target, updated.TargetTable);
        Assert.NotEqual(original, updated);
    }
}
