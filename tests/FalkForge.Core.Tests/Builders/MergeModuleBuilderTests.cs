using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class MergeModuleBuilderTests
{
    [Fact]
    public void Build_WithAllProperties_ReturnsPopulatedModel()
    {
        var id = Guid.NewGuid();
        var version = new Version(2, 1, 0);

        var builder = new MergeModuleBuilder();
        var result = builder
            .Id(id)
            .Language(1033)
            .Version(version)
            .Manufacturer("TestCorp")
            .Description("A test merge module")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal(id, model.Id);
        Assert.Equal(1033, model.Language);
        Assert.Equal(version, model.Version);
        Assert.Equal("TestCorp", model.Manufacturer);
        Assert.Equal("A test merge module", model.Description);
    }

    [Fact]
    public void Build_DefaultLanguage_Is1033()
    {
        var result = new MergeModuleBuilder()
            .Id(Guid.NewGuid())
            .Version(new Version(1, 0, 0))
            .Manufacturer("Corp")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(1033, result.Value.Language);
    }

    [Fact]
    public void Build_WithComponents_AddsToList()
    {
        var result = new MergeModuleBuilder()
            .Id(Guid.NewGuid())
            .Language(1033)
            .Version(new Version(1, 0, 0))
            .Manufacturer("Corp")
            .Component("Comp1")
            .Component("Comp2")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal(2, model.Components.Count);
        Assert.Contains("Comp1", model.Components);
        Assert.Contains("Comp2", model.Components);
    }

    [Fact]
    public void Build_WithDependencies_AddsToList()
    {
        var result = new MergeModuleBuilder()
            .Id(Guid.NewGuid())
            .Language(1033)
            .Version(new Version(1, 0, 0))
            .Manufacturer("Corp")
            .Dependency("Dep1")
            .Dependency("Dep2")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal(2, model.Dependencies.Count);
        Assert.Contains("Dep1", model.Dependencies);
        Assert.Contains("Dep2", model.Dependencies);
    }

    [Fact]
    public void Build_NoComponents_ReturnsEmptyList()
    {
        var result = new MergeModuleBuilder()
            .Id(Guid.NewGuid())
            .Language(1033)
            .Version(new Version(1, 0, 0))
            .Manufacturer("Corp")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Empty(model.Components);
        Assert.Empty(model.Dependencies);
    }

    [Fact]
    public void Build_EmptyGuid_ReturnsFailure()
    {
        var result = new MergeModuleBuilder()
            .Language(1033)
            .Version(new Version(1, 0, 0))
            .Manufacturer("Corp")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("MSM001", result.Error.Message);
    }

    [Fact]
    public void Build_EmptyManufacturer_ReturnsFailure()
    {
        var result = new MergeModuleBuilder()
            .Id(Guid.NewGuid())
            .Language(1033)
            .Version(new Version(1, 0, 0))
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("MSM004", result.Error.Message);
    }
}
