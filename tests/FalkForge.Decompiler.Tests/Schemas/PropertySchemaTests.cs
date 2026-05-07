using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests.Schemas;

public sealed class PropertySchemaTests
{
    [Fact]
    public void Read_SingleRow_MapsCorrectly()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property", [["MyProp", "MyValue"]]);

        var result = TableReadEngine.ReadOne(PropertySchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("MyProp", result.Value[0].Property);
        Assert.Equal("MyValue", result.Value[0].Value);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmpty()
    {
        using var access = new MockMsiTableAccess();

        var result = TableReadEngine.ReadOne(PropertySchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_MultipleRows_PreservesAll()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName", "Acme"],
                ["Manufacturer", "Acme Corp"],
                ["ProductVersion", "1.0.0"],
            ]);

        var result = TableReadEngine.ReadOne(PropertySchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
    }
}
