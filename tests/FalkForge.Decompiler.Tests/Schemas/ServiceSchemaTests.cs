using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests.Schemas;

public sealed class ServiceSchemaTests
{
    [Fact]
    public void Read_FullRow_MapsAllFields()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("ServiceInstall",
            [
                ["MySvc", "MySvc", "My Service", "16", "2", "1", null, null, "LocalSystem", null, null, "comp1", "A service"]
            ]);

        var result = TableReadEngine.ReadOne(ServiceSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        var row = result.Value[0];
        Assert.Equal("MySvc", row.ServiceInstall);
        Assert.Equal("MySvc", row.Name);
        Assert.Equal("My Service", row.DisplayName);
        Assert.Equal(16, row.ServiceType);
        Assert.Equal(2, row.StartType);
        Assert.Equal(1, row.ErrorControl);
        Assert.Equal("LocalSystem", row.StartName);
        Assert.Equal("comp1", row.Component_);
        Assert.Equal("A service", row.Description_);
    }

    [Fact]
    public void Read_NullOptionalFields_Handled()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("ServiceInstall",
            [
                ["MySvc", "MySvc", null, "16", "2", "1", null, null, null, null, null, "comp1", null]
            ]);

        var result = TableReadEngine.ReadOne(ServiceSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value[0].DisplayName);
        Assert.Null(result.Value[0].Description_);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmpty()
    {
        using var access = new MockMsiTableAccess();
        var result = TableReadEngine.ReadOne(ServiceSchema.Schema, access);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
