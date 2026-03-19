using FalkForge.Studio.Editors.RegistryEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class RegistryEditorViewModelTests
{
    private static StudioProject CreateProjectWithEntries()
    {
        var project = StudioProjectLoader.NewProject();
        project.Registry.Add(new RegistryEntrySection
        {
            Root = "LocalMachine",
            Key = @"SOFTWARE\TestApp",
            ValueName = "InstallPath",
            ValueType = "String",
            Value = @"C:\Program Files\TestApp"
        });
        project.Registry.Add(new RegistryEntrySection
        {
            Root = "CurrentUser",
            Key = @"SOFTWARE\TestApp",
            ValueName = "Version",
            ValueType = "String",
            Value = "1.0.0"
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
        var project = CreateProjectWithEntries();
        var vm = new RegistryEditorViewModel(project);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void AddEntry_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new RegistryEditorViewModel(project);
        vm.AddEntry();
        Assert.Single(vm.Entries);
        Assert.Single(project.Registry);
        Assert.Equal(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_RemovesEntry()
    {
        var project = CreateProjectWithEntries();
        var vm = new RegistryEditorViewModel(project);
        vm.SelectedEntry = vm.Entries[0];
        vm.RemoveSelected();
        Assert.Single(vm.Entries);
        Assert.Single(project.Registry);
        Assert.Equal("CurrentUser", vm.Entries[0].Root);
    }

    [Fact]
    public void RemoveSelected_NullSelected_NoOp()
    {
        var project = CreateProjectWithEntries();
        var vm = new RegistryEditorViewModel(project);
        vm.SelectedEntry = null;
        vm.RemoveSelected();
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void EntryViewModel_PropertyChange_UpdatesModel()
    {
        var model = new RegistryEntrySection { Root = "LocalMachine", Key = @"SOFTWARE\Test" };
        var vm = new RegistryEntryViewModel(model);
        vm.Key = @"SOFTWARE\Updated";
        Assert.Equal(@"SOFTWARE\Updated", model.Key);
    }

    [Fact]
    public void BuildModel_WithRegistryEntry_Success()
    {
        var project = CreateValidBuildProject();
        project.Registry.Add(new RegistryEntrySection
        {
            Root = "LocalMachine",
            Key = @"SOFTWARE\TestApp",
            ValueName = "InstallPath",
            ValueType = "String",
            Value = @"C:\Program Files\TestApp"
        });

        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void BuildModel_InvalidRegistryRoot_Failure()
    {
        var project = CreateValidBuildProject();
        project.Registry.Add(new RegistryEntrySection
        {
            Root = "InvalidRoot",
            Key = @"SOFTWARE\TestApp",
            ValueName = "InstallPath",
            ValueType = "String",
            Value = "test"
        });

        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("registry root", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
