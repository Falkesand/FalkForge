using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests.Schemas;

public sealed class FeatureSchemaTests
{
    [Fact]
    public void Read_FullRow_MapsAllFields()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Feature",
            [
                ["MainFeature", null, "Main", "Main feature", "2", "1", "INSTALLFOLDER", "0"]
            ]);

        var result = TableReadEngine.ReadOne(FeatureSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        var row = result.Value[0];
        Assert.Equal("MainFeature", row.Feature);
        Assert.Null(row.Feature_Parent);
        Assert.Equal("Main", row.Title);
        Assert.Equal("Main feature", row.Description);
        Assert.Equal(2, row.Display);
        Assert.Equal(1, row.Level);
        Assert.Equal("INSTALLFOLDER", row.Directory_);
        Assert.Equal(0, row.Attributes);
    }

    [Fact]
    public void Read_ChildFeature_HasParent()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Feature",
            [
                ["Child", "Parent", "Child Feature", null, "0", "1", null, "0"]
            ]);

        var result = TableReadEngine.ReadOne(FeatureSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Equal("Parent", result.Value[0].Feature_Parent);
    }

    [Fact]
    public void FeatureComponents_Read_MapsFeatureAndComponent()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("FeatureComponents",
            [
                ["MainFeature", "comp1"],
                ["MainFeature", "comp2"],
            ]);

        var result = TableReadEngine.ReadOne(FeatureComponentsSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("MainFeature", result.Value[0].Feature_);
        Assert.Equal("comp1", result.Value[0].Component_);
    }
}
