using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class TransformBuilderTests
{
    [Fact]
    public void Build_WithAllProperties_ReturnsPopulatedModel()
    {
        var result = new TransformBuilder()
            .Id("MyTransform")
            .BaseMsi(@"C:\base\product.msi")
            .TargetMsi(@"C:\target\product.msi")
            .Description("Customization transform")
            .SetProperty("INSTALLLEVEL", "200")
            .SetProperty("COMPANYNAME", "TestCorp")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal("MyTransform", model.Id);
        Assert.Equal(@"C:\base\product.msi", model.BaseMsiPath);
        Assert.Equal(@"C:\target\product.msi", model.TargetMsiPath);
        Assert.Equal("Customization transform", model.Description);
        Assert.Equal(2, model.PropertyChanges.Count);
        Assert.Equal("200", model.PropertyChanges["INSTALLLEVEL"]);
        Assert.Equal("TestCorp", model.PropertyChanges["COMPANYNAME"]);
    }

    [Fact]
    public void Build_NoPropertyChanges_ReturnsEmptyDictionary()
    {
        var result = new TransformBuilder()
            .BaseMsi("base.msi")
            .TargetMsi("target.msi")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.PropertyChanges);
    }

    [Fact]
    public void Build_NullId_AllowsNullId()
    {
        var result = new TransformBuilder()
            .BaseMsi("base.msi")
            .TargetMsi("target.msi")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Id);
    }

    [Fact]
    public void SetProperty_OverwritesExistingKey()
    {
        var result = new TransformBuilder()
            .BaseMsi("base.msi")
            .TargetMsi("target.msi")
            .SetProperty("KEY", "value1")
            .SetProperty("KEY", "value2")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Single(model.PropertyChanges);
        Assert.Equal("value2", model.PropertyChanges["KEY"]);
    }

    [Fact]
    public void Build_FluentChaining_ReturnsBuilder()
    {
        var builder = new TransformBuilder();
        var same = builder
            .Id("T1")
            .BaseMsi("base.msi")
            .TargetMsi("target.msi")
            .Description("desc")
            .SetProperty("P1", "V1");

        Assert.Same(builder, same);
    }

    [Fact]
    public void Build_EmptyBaseMsiPath_ReturnsFailure()
    {
        var result = new TransformBuilder()
            .TargetMsi("target.msi")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("MST001", result.Error.Message);
    }

    [Fact]
    public void Build_EmptyTargetMsiPath_ReturnsFailure()
    {
        var result = new TransformBuilder()
            .BaseMsi("base.msi")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("MST002", result.Error.Message);
    }
}
