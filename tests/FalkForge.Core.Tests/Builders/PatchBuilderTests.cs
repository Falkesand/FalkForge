using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class PatchBuilderTests
{
    [Fact]
    public void Build_WithAllProperties_ReturnsPopulatedModel()
    {
        var id = Guid.NewGuid();
        var productCode = Guid.NewGuid();

        var result = new PatchBuilder()
            .Id(id)
            .Classification(PatchClassification.Hotfix)
            .Description("Critical security fix")
            .Manufacturer("TestCorp")
            .TargetProduct(productCode)
            .TargetVersion("1.0.0")
            .UpdatedVersion("1.0.1")
            .TargetMsi(@"C:\old\product.msi")
            .UpdatedMsi(@"C:\new\product.msi")
            .AllowRemoval()
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal(id, model.Id);
        Assert.Equal(PatchClassification.Hotfix, model.Classification);
        Assert.Equal("Critical security fix", model.Description);
        Assert.Equal("TestCorp", model.Manufacturer);
        Assert.Equal(productCode, model.TargetProductCode);
        Assert.Equal("1.0.0", model.TargetVersion);
        Assert.Equal("1.0.1", model.UpdatedVersion);
        Assert.Equal(@"C:\old\product.msi", model.TargetMsiPath);
        Assert.Equal(@"C:\new\product.msi", model.UpdatedMsiPath);
        Assert.True(model.AllowRemoval);
    }

    [Fact]
    public void Build_DefaultClassification_IsUpdate()
    {
        var result = new PatchBuilder()
            .Id(Guid.NewGuid())
            .TargetMsi("old.msi")
            .UpdatedMsi("new.msi")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(PatchClassification.Update, result.Value.Classification);
    }

    [Fact]
    public void Build_DefaultAllowRemoval_IsFalse()
    {
        var result = new PatchBuilder()
            .Id(Guid.NewGuid())
            .TargetMsi("old.msi")
            .UpdatedMsi("new.msi")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AllowRemoval);
    }

    [Fact]
    public void Build_SecurityUpdateClassification_SetsCorrectly()
    {
        var result = new PatchBuilder()
            .Id(Guid.NewGuid())
            .Classification(PatchClassification.SecurityUpdate)
            .TargetMsi("old.msi")
            .UpdatedMsi("new.msi")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(PatchClassification.SecurityUpdate, result.Value.Classification);
    }

    [Fact]
    public void Build_AllowRemovalFalse_SetsCorrectly()
    {
        var result = new PatchBuilder()
            .Id(Guid.NewGuid())
            .TargetMsi("old.msi")
            .UpdatedMsi("new.msi")
            .AllowRemoval(false)
            .Build();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AllowRemoval);
    }

    [Fact]
    public void Build_EmptyGuid_ReturnsFailure()
    {
        var result = new PatchBuilder()
            .TargetMsi("old.msi")
            .UpdatedMsi("new.msi")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("MSP004", result.Error.Message);
    }

    [Fact]
    public void Build_EmptyPaths_ReturnsFailure()
    {
        var result = new PatchBuilder()
            .Id(Guid.NewGuid())
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("MSP001", result.Error.Message);
    }
}
