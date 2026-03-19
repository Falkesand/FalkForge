using FalkForge.Studio.Editors.EnvironmentEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class EnvironmentEditorViewModelTests
{
    private static StudioProject CreateProjectWithEnvironment()
    {
        var project = StudioProjectLoader.NewProject();
        project.Environment.Add(new EnvironmentVariableSection
        {
            Name = "PATH",
            Value = "[INSTALLDIR]bin",
            Action = "Append",
            IsSystem = true
        });
        project.Environment.Add(new EnvironmentVariableSection
        {
            Name = "MY_VAR",
            Value = "hello",
            Action = "Set",
            IsSystem = false
        });
        return project;
    }

    private static StudioProject CreateValidBuildProject()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        return project;
    }

    [Fact]
    public void Constructor_LoadsEntries()
    {
        var project = CreateProjectWithEnvironment();
        var vm = new EnvironmentEditorViewModel(project);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void AddEntry_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new EnvironmentEditorViewModel(project);
        vm.AddEntry();
        Assert.Single(vm.Entries);
        Assert.Single(project.Environment);
        Assert.Equal(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_RemovesEntry()
    {
        var project = CreateProjectWithEnvironment();
        var vm = new EnvironmentEditorViewModel(project);
        vm.SelectedEntry = vm.Entries[0];
        vm.RemoveSelected();
        Assert.Single(vm.Entries);
        Assert.Single(project.Environment);
        Assert.Equal("MY_VAR", vm.Entries[0].Name);
    }

    [Fact]
    public void RemoveSelected_NullSelected_NoOp()
    {
        var project = CreateProjectWithEnvironment();
        var vm = new EnvironmentEditorViewModel(project);
        vm.SelectedEntry = null;
        vm.RemoveSelected();
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void EntryViewModel_PropertyChange_UpdatesModel()
    {
        var model = new EnvironmentVariableSection { Name = "PATH", Value = "old" };
        var vm = new EnvironmentEntryViewModel(model);
        vm.Value = "new";
        Assert.Equal("new", model.Value);
    }

    [Fact]
    public void BuildModel_WithEnvironmentEntry_Success()
    {
        var project = CreateValidBuildProject();
        project.Environment.Add(new EnvironmentVariableSection
        {
            Name = "MY_VAR",
            Value = "hello",
            Action = "Set",
            IsSystem = true
        });

        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
    }
}
