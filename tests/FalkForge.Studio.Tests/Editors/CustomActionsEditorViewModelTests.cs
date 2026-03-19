using FalkForge.Studio.Editors.CustomActionsEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class CustomActionsEditorViewModelTests
{
    private static StudioProject CreateProjectWithCustomActions()
    {
        var project = StudioProjectLoader.NewProject();
        project.CustomActions.Add(new CustomActionSection
        {
            Id = "SetConfig",
            Type = "SetProperty",
            Source = "CONFIG_PATH",
            Target = "[INSTALLDIR]config.xml"
        });
        project.CustomActions.Add(new CustomActionSection
        {
            Id = "RunSetup",
            Type = "ExeFromBinary",
            Source = "setup.exe",
            Deferred = true
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
        var project = CreateProjectWithCustomActions();
        var vm = new CustomActionsEditorViewModel(project);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void AddEntry_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new CustomActionsEditorViewModel(project);
        vm.AddEntry();
        Assert.Single(vm.Entries);
        Assert.Single(project.CustomActions);
        Assert.Equal(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_RemovesEntry()
    {
        var project = CreateProjectWithCustomActions();
        var vm = new CustomActionsEditorViewModel(project);
        vm.SelectedEntry = vm.Entries[0];
        vm.RemoveSelected();
        Assert.Single(vm.Entries);
        Assert.Single(project.CustomActions);
        Assert.Equal("RunSetup", vm.Entries[0].Id);
    }

    [Fact]
    public void RemoveSelected_NullSelected_NoOp()
    {
        var project = CreateProjectWithCustomActions();
        var vm = new CustomActionsEditorViewModel(project);
        vm.SelectedEntry = null;
        vm.RemoveSelected();
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void EntryViewModel_PropertyChange_UpdatesModel()
    {
        var model = new CustomActionSection { Id = "CA1", Type = "SetProperty" };
        var vm = new CustomActionEntryViewModel(model);
        vm.Source = "PROP_NAME";
        Assert.Equal("PROP_NAME", model.Source);
    }

    [Fact]
    public void BuildModel_WithCustomActionEntry_Success()
    {
        var project = CreateValidBuildProject();
        project.CustomActions.Add(new CustomActionSection
        {
            Id = "SetConfig",
            Type = "SetProperty",
            Source = "CONFIG_PATH",
            Target = "[INSTALLDIR]config.xml",
            Deferred = true,
            NoImpersonate = true
        });

        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
    }
}
