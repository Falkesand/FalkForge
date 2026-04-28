using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class CellValueTests
{
    [Fact]
    public void Null_singleton_pattern_matches()
    {
        CellValue value = new CellValue.Null();

        Assert.IsType<CellValue.Null>(value);
    }

    [Fact]
    public void IntValue_preserves_value()
    {
        CellValue.IntValue cell = new(42);

        Assert.Equal(42, cell.Value);
    }

    [Fact]
    public void IntValue_pattern_match_extracts_int()
    {
        CellValue cell = new CellValue.IntValue(7);

        Assert.True(cell is CellValue.IntValue { Value: 7 });
    }

    [Fact]
    public void StringValue_preserves_value()
    {
        CellValue.StringValue cell = new("Property");

        Assert.Equal("Property", cell.Value);
    }

    [Fact]
    public void ForeignKey_preserves_target_table_and_key()
    {
        TableId table = TableId.Create("Component").Value;
        CellValue.ForeignKey cell = new(table, "MyComponent");

        Assert.Equal(table, cell.TargetTable);
        Assert.Equal("MyComponent", cell.TargetKey);
    }

    [Fact]
    public void ForeignKey_round_trip_via_pattern_match()
    {
        TableId target = TableId.Create("Directory").Value;
        CellValue cell = new CellValue.ForeignKey(target, "INSTALLDIR");

        Assert.True(cell is CellValue.ForeignKey { TargetKey: "INSTALLDIR" });
    }

    [Fact]
    public void StreamRef_preserves_stream_name()
    {
        CellValue.StreamRef cell = new("Binary.MyDll");

        Assert.Equal("Binary.MyDll", cell.StreamName);
    }

    [Fact]
    public void Equal_int_values_compare_equal()
    {
        CellValue a = new CellValue.IntValue(5);
        CellValue b = new CellValue.IntValue(5);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_subtypes_are_not_equal()
    {
        CellValue a = new CellValue.IntValue(0);
        CellValue b = new CellValue.Null();

        Assert.NotEqual(a, b);
    }
}
