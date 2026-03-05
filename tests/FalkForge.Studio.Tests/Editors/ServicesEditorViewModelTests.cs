using FalkForge.Studio.Editors.ServicesEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class ServicesEditorViewModelTests
{
    private static StudioProject CreateProjectWithServices()
    {
        var project = StudioProjectLoader.NewProject();
        project.Services.Add(new ServiceSection
        {
            Name = "MySvc",
            DisplayName = "My Service",
            Executable = "[INSTALLDIR]svc.exe",
            StartMode = "Automatic",
            Account = "LocalSystem"
        });
        project.Services.Add(new ServiceSection
        {
            Name = "OtherSvc",
            DisplayName = "Other Service",
            Executable = "[INSTALLDIR]other.exe",
            StartMode = "Manual",
            Account = "NetworkService"
        });
        return project;
    }

    private static StudioProject CreateValidBuildProject()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";
        project.Product.Version = "1.0.0";
        return project;
    }

    [Fact]
    public void Constructor_LoadsEntries()
    {
        var project = CreateProjectWithServices();
        var vm = new ServicesEditorViewModel(project);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void AddEntry_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new ServicesEditorViewModel(project);
        vm.AddEntry();
        Assert.Single(vm.Entries);
        Assert.Single(project.Services);
        Assert.Equal(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_RemovesEntry()
    {
        var project = CreateProjectWithServices();
        var vm = new ServicesEditorViewModel(project);
        vm.SelectedEntry = vm.Entries[0];
        vm.RemoveSelected();
        Assert.Single(vm.Entries);
        Assert.Single(project.Services);
        Assert.Equal("OtherSvc", vm.Entries[0].Name);
    }

    [Fact]
    public void RemoveSelected_NullSelected_NoOp()
    {
        var project = CreateProjectWithServices();
        var vm = new ServicesEditorViewModel(project);
        vm.SelectedEntry = null;
        vm.RemoveSelected();
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void EntryViewModel_PropertyChange_UpdatesModel()
    {
        var model = new ServiceSection { Name = "Svc1", DisplayName = "Service One" };
        var vm = new ServiceEntryViewModel(model);
        vm.DisplayName = "Updated Name";
        Assert.Equal("Updated Name", model.DisplayName);
    }

    [Fact]
    public void BuildModel_WithServiceEntry_Success()
    {
        var project = CreateValidBuildProject();
        project.Services.Add(new ServiceSection
        {
            Name = "MySvc",
            DisplayName = "My Service",
            Executable = "[INSTALLDIR]svc.exe",
            StartMode = "Automatic",
            Account = "LocalSystem"
        });

        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void BuildModel_InvalidStartMode_Failure()
    {
        var project = CreateValidBuildProject();
        project.Services.Add(new ServiceSection
        {
            Name = "MySvc",
            DisplayName = "My Service",
            Executable = "[INSTALLDIR]svc.exe",
            StartMode = "InvalidMode",
            Account = "LocalSystem"
        });

        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("start mode", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
