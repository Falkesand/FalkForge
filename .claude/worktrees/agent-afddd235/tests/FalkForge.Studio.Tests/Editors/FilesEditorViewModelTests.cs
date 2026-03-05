using FalkForge.Studio.Editors.FilesEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class FilesEditorViewModelTests
{
    private static StudioProject CreateProject()
    {
        var project = StudioProjectLoader.NewProject();
        project.Features[0].Files.Add(new FileEntry { Source = "app.exe" });
        project.Features[0].Files.Add(new FileEntry { Source = "lib.dll" });
        return project;
    }

    [Fact]
    public void Constructor_LoadsFilesFromProject()
    {
        var project = CreateProject();
        var vm = new FilesEditorViewModel(project);
        Assert.Equal(2, vm.Files.Count);
        Assert.Equal("app.exe", vm.Files[0].Source);
        Assert.Equal("lib.dll", vm.Files[1].Source);
    }

    [Fact]
    public void AddFile_AddsToCollectionAndModel()
    {
        var project = CreateProject();
        var vm = new FilesEditorViewModel(project);
        vm.AddFile("new.dll", "Main");
        Assert.Equal(3, vm.Files.Count);
        Assert.Equal(3, project.Features[0].Files.Count);
        Assert.Equal("new.dll", vm.Files[2].Source);
    }

    [Fact]
    public void RemoveSelected_RemovesFromCollectionAndModel()
    {
        var project = CreateProject();
        var vm = new FilesEditorViewModel(project);
        vm.SelectedFile = vm.Files[0];
        vm.RemoveSelected();
        Assert.Single(vm.Files);
        Assert.Single(project.Features[0].Files);
        Assert.Equal("lib.dll", vm.Files[0].Source);
    }

    [Fact]
    public void RemoveSelected_WhenNull_DoesNothing()
    {
        var project = CreateProject();
        var vm = new FilesEditorViewModel(project);
        vm.SelectedFile = null;
        vm.RemoveSelected();
        Assert.Equal(2, vm.Files.Count);
    }

    [Fact]
    public void FileEntry_SourceChange_UpdatesModel()
    {
        var model = new FileEntry { Source = "old.dll" };
        var vm = new FileEntryViewModel(model, "Main");
        vm.Source = "new.dll";
        Assert.Equal("new.dll", model.Source);
    }

    [Fact]
    public void AddFile_SetsSelectedFile()
    {
        var project = CreateProject();
        var vm = new FilesEditorViewModel(project);
        vm.AddFile("selected.dll", "Main");
        Assert.NotNull(vm.SelectedFile);
        Assert.Equal("selected.dll", vm.SelectedFile!.Source);
    }
}
