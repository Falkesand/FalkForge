using FalkForge.Studio.Editors.ShortcutsEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class ShortcutsEditorViewModelTests
{
    private static StudioProject CreateProjectWithShortcuts()
    {
        var project = StudioProjectLoader.NewProject();
        project.Shortcuts.Add(new ShortcutSection
        {
            Name = "MyApp",
            TargetFile = "[INSTALLDIR]app.exe",
            Desktop = true,
            StartMenu = true
        });
        project.Shortcuts.Add(new ShortcutSection
        {
            Name = "MyTool",
            TargetFile = "[INSTALLDIR]tool.exe",
            StartMenu = true
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
        var project = CreateProjectWithShortcuts();
        var vm = new ShortcutsEditorViewModel(project);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void AddEntry_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new ShortcutsEditorViewModel(project);
        vm.AddEntry();
        Assert.Single(vm.Entries);
        Assert.Single(project.Shortcuts);
        Assert.Equal(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_RemovesEntry()
    {
        var project = CreateProjectWithShortcuts();
        var vm = new ShortcutsEditorViewModel(project);
        vm.SelectedEntry = vm.Entries[0];
        vm.RemoveSelected();
        Assert.Single(vm.Entries);
        Assert.Single(project.Shortcuts);
        Assert.Equal("MyTool", vm.Entries[0].Name);
    }

    [Fact]
    public void RemoveSelected_NullSelected_NoOp()
    {
        var project = CreateProjectWithShortcuts();
        var vm = new ShortcutsEditorViewModel(project);
        vm.SelectedEntry = null;
        vm.RemoveSelected();
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void EntryViewModel_PropertyChange_UpdatesModel()
    {
        var model = new ShortcutSection { Name = "App", TargetFile = "[INSTALLDIR]app.exe" };
        var vm = new ShortcutEntryViewModel(model);
        vm.Description = "Updated description";
        Assert.Equal("Updated description", model.Description);
    }

    [Fact]
    public void BuildModel_WithShortcutEntry_Success()
    {
        var project = CreateValidBuildProject();
        project.Shortcuts.Add(new ShortcutSection
        {
            Name = "MyApp",
            TargetFile = "[INSTALLDIR]app.exe",
            Desktop = true,
            StartMenu = true
        });

        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void BuildModel_MissingTargetFile_Failure()
    {
        var project = CreateValidBuildProject();
        project.Shortcuts.Add(new ShortcutSection
        {
            Name = "MyApp",
            TargetFile = ""
        });

        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("target file", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
