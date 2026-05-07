using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests.Schemas;

public sealed class RegistrySchemaTests
{
    [Fact]
    public void Read_FullRow_MapsAllFields()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Registry",
            [
                ["reg1", "2", @"SOFTWARE\MyApp", "InstallPath", @"C:\MyApp", "comp1"]
            ]);

        var result = TableReadEngine.ReadOne(RegistrySchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        var row = result.Value[0];
        Assert.Equal("reg1", row.Registry);
        Assert.Equal(2, row.Root);
        Assert.Equal(@"SOFTWARE\MyApp", row.Key);
        Assert.Equal("InstallPath", row.Name);
        Assert.Equal(@"C:\MyApp", row.Value);
        Assert.Equal("comp1", row.Component_);
    }

    [Fact]
    public void Read_NullableFields_Allowed()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Registry", [["reg1", "2", @"SOFTWARE\MyApp", null, null, null]]);

        var result = TableReadEngine.ReadOne(RegistrySchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value[0].Name);
        Assert.Null(result.Value[0].Value);
        Assert.Null(result.Value[0].Component_);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmpty()
    {
        using var access = new MockMsiTableAccess();
        var result = TableReadEngine.ReadOne(RegistrySchema.Schema, access);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
