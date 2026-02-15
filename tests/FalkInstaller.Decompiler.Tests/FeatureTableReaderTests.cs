using FalkInstaller.Decompiler.TableReaders;
using Xunit;

namespace FalkInstaller.Decompiler.Tests;

public sealed class FeatureTableReaderTests
{
    [Fact]
    public void Read_EmptyTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Feature", []);

        var result = FeatureTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess();

        var result = FeatureTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_SingleFeature()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Feature",
            [
                // Feature, Feature_Parent, Title, Description, Display, Level, Directory_, Attributes
                ["Complete", null, "Complete Installation", "Full install", "1", "1", "INSTALLFOLDER", "0"]
            ]);

        var result = FeatureTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("Complete", result.Value[0].Id);
        Assert.Equal("Complete Installation", result.Value[0].Title);
        Assert.Equal("Full install", result.Value[0].Description);
        Assert.Equal(1, result.Value[0].DisplayLevel);
    }

    [Fact]
    public void Read_NestedFeatures_BuildsTree()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Feature",
            [
                ["Main", null, "Main Feature", null, "1", "1", null, "0"],
                ["Sub1", "Main", "Sub Feature 1", "First sub", "2", "1", null, "0"],
                ["Sub2", "Main", "Sub Feature 2", "Second sub", "3", "1", null, "0"]
            ]);

        var result = FeatureTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value); // Only top-level
        Assert.Equal("Main", result.Value[0].Id);
        Assert.Equal(2, result.Value[0].Children.Count);
        Assert.Equal("Sub1", result.Value[0].Children[0].Id);
        Assert.Equal("Sub2", result.Value[0].Children[1].Id);
    }

    [Fact]
    public void Read_RequiredFeature_Level0()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Feature",
            [
                ["Required", null, "Required Feature", null, "0", "0", null, "0"]
            ]);

        var result = FeatureTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.True(result.Value[0].IsRequired);
    }

    [Fact]
    public void Read_WithComponentRefs()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Feature",
            [
                ["Main", null, "Main", null, "1", "1", null, "0"]
            ])
            .WithTable("FeatureComponents",
            [
                ["Main", "comp1"],
                ["Main", "comp2"]
            ]);

        var result = FeatureTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(2, result.Value[0].ComponentRefs.Count);
        Assert.Contains("comp1", result.Value[0].ComponentRefs);
        Assert.Contains("comp2", result.Value[0].ComponentRefs);
    }
}
