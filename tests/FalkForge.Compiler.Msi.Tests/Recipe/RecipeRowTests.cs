using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class RecipeRowTests
{
    [Fact]
    public void Construct_with_cells_preserves_values()
    {
        ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
            new CellValue.StringValue("ProductCode"),
            new CellValue.StringValue("{1234}"));

        RecipeRow row = new() { Cells = cells };

        Assert.Equal(2, row.Cells.Length);
        Assert.Equal(cells, row.Cells);
    }

    [Fact]
    public void Construct_with_empty_cells_succeeds()
    {
        RecipeRow row = new() { Cells = ImmutableArray<CellValue>.Empty };

        Assert.True(row.Cells.IsEmpty);
    }

    [Fact]
    public void Equal_rows_compare_equal_by_value()
    {
        ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(new CellValue.IntValue(1));
        RecipeRow a = new() { Cells = cells };
        RecipeRow b = new() { Cells = cells };

        Assert.Equal(a, b);
    }
}
