using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests.Schemas;

public sealed class ComponentSchemaTests
{
    [Fact]
    public void Read_FullRow_MapsAllFields()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Component",
            [
                ["WixComp1", "{11111111-1111-1111-1111-111111111111}", "INSTALLFOLDER", "8", "VersionNT>=600", "file1.exe"]
            ]);

        var result = TableReadEngine.ReadOne(ComponentSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        var row = result.Value[0];
        Assert.Equal("WixComp1", row.Component);
        Assert.Equal("{11111111-1111-1111-1111-111111111111}", row.ComponentId);
        Assert.Equal("INSTALLFOLDER", row.Directory_);
        Assert.Equal(8, row.Attributes);
        Assert.Equal("VersionNT>=600", row.Condition);
        Assert.Equal("file1.exe", row.KeyPath);
    }

    [Fact]
    public void Read_NullOptionalFields_Handled()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Component", [["comp1", null, "INSTALLFOLDER", "0", null, null]]);

        var result = TableReadEngine.ReadOne(ComponentSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value[0].ComponentId);
        Assert.Null(result.Value[0].Condition);
        Assert.Null(result.Value[0].KeyPath);
    }

    [Fact]
    public void Read_MultipleRows_PreservesOrder()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Component",
            [
                ["c1", null, "D1", "0", null, null],
                ["c2", null, "D2", "4", null, null],
            ]);

        var result = TableReadEngine.ReadOne(ComponentSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("c1", result.Value[0].Component);
        Assert.Equal("c2", result.Value[1].Component);
    }
}
