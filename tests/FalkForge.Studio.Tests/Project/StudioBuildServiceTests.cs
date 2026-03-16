using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Project;

public class StudioBuildServiceTests
{
    [Fact]
    public void BuildModel_MissingName_ReturnsFailure()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("name", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModel_MissingManufacturer_ReturnsFailure()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Manufacturer = "";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("manufacturer", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModel_InvalidVersion_ReturnsFailure()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Product.Version = "bad";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("version", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModel_InvalidUpgradeCode_ReturnsFailure()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Product.UpgradeCode = "not-a-guid";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("upgrade code", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModel_ValidProject_ReturnsSuccess()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";
        project.Product.Version = "1.0.0";
        project.InstallDirectory = "TestCorp/TestApp";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
        Assert.Equal("TestApp", result.Value.Name);
        Assert.Equal("TestCorp", result.Value.Manufacturer);
    }

    [Fact]
    public void BuildModel_SetsArchitecture()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Product.Architecture = "arm64";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
        Assert.Equal(FalkForge.ProcessorArchitecture.Arm64, result.Value.Architecture);
    }

    [Fact]
    public void BuildModel_SetsScope()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Product.Scope = "perUser";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
        Assert.Equal(FalkForge.InstallScope.PerUser, result.Value.Scope);
    }

    [Fact]
    public void BuildModel_SetsDialogSet()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Ui.DialogSet = "FeatureTree";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
        Assert.Equal(FalkForge.Models.MsiDialogSet.FeatureTree, result.Value.DialogSet);
    }

    [Fact]
    public void BuildModel_ProductLicenseFile_TakesPrecedenceOverUiLicenseFile()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Product.LicenseFile = "product-license.rtf";
        project.Ui.LicenseFile = "ui-license.rtf";
        var result = StudioBuildService.BuildModel(project, @"C:\base");
        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\base\product-license.rtf", result.Value.LicenseFile);
    }

    [Fact]
    public void BuildModel_UiLicenseFile_UsedWhenProductLicenseFileIsNull()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Product.LicenseFile = null;
        project.Ui.LicenseFile = "ui-license.rtf";
        var result = StudioBuildService.BuildModel(project, @"C:\base");
        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\base\ui-license.rtf", result.Value.LicenseFile);
    }

    [Fact]
    public void BuildModel_EmptyFeatureId_ReturnsFailure()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Features[0].Id = "";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("Feature", result.Error.Message);
    }
}
