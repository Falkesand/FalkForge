using FalkForge.Studio.Editors.BundleSettingsEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class BundleSettingsEditorViewModelTests
{
    [Fact]
    public void Constructor_SetsDefaults()
    {
        var section = new BundleSettingsSection();
        var vm = new BundleSettingsEditorViewModel(section);

        Assert.Equal("", vm.Name);
        Assert.Equal("", vm.Manufacturer);
        Assert.Equal("1.0.0", vm.Version);
        Assert.Null(vm.UpgradeCode);
        Assert.Equal("perMachine", vm.Scope);
        Assert.Equal("BuiltIn", vm.UiType);
        Assert.Null(vm.LicenseFile);
        Assert.Equal(0, vm.DownloadThrottle);
    }

    [Fact]
    public void PropertyChange_UpdatesModel()
    {
        var section = new BundleSettingsSection();
        var vm = new BundleSettingsEditorViewModel(section);

        vm.Name = "MyBundle";
        Assert.Equal("MyBundle", section.Name);

        vm.Manufacturer = "Corp";
        Assert.Equal("Corp", section.Manufacturer);

        vm.Version = "2.0.0";
        Assert.Equal("2.0.0", section.Version);

        vm.Scope = "perUser";
        Assert.Equal("perUser", section.Scope);

        vm.UiType = "Silent";
        Assert.Equal("Silent", section.UiType);
    }

    [Fact]
    public void BuildBundleModel_ValidSettings_Success()
    {
        var project = StudioProjectLoader.NewProject();
        project.ProjectType = "bundle";
        project.BundleSettings = new BundleSettingsSection
        {
            Name = "TestBundle",
            Manufacturer = "Corp"
        };

        var result = StudioBuildService.BuildBundleModel(project, "C:\\temp");
        Assert.True(result.IsSuccess);
        Assert.Equal("TestBundle", result.Value.Name);
        Assert.Equal("Corp", result.Value.Manufacturer);
    }

    [Fact]
    public void BuildBundleModel_MissingName_Failure()
    {
        var project = StudioProjectLoader.NewProject();
        project.ProjectType = "bundle";
        project.BundleSettings = new BundleSettingsSection
        {
            Name = "",
            Manufacturer = "Corp"
        };

        var result = StudioBuildService.BuildBundleModel(project, "C:\\temp");
        Assert.True(result.IsFailure);
        Assert.Contains("name", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBundleModel_NullSettings_Failure()
    {
        var project = StudioProjectLoader.NewProject();
        project.ProjectType = "bundle";
        project.BundleSettings = null;

        var result = StudioBuildService.BuildBundleModel(project, "C:\\temp");
        Assert.True(result.IsFailure);
        Assert.Contains("required", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
