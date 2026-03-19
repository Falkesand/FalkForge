using FalkForge.Studio.Editors.BundlePackagesEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class BundlePackagesEditorViewModelTests
{
    private static StudioProject CreateBundleProject(params BundlePackageSection[] packages)
    {
        var project = StudioProjectLoader.NewProject();
        project.ProjectType = "bundle";
        project.BundleSettings = new BundleSettingsSection { Name = "Test", Manufacturer = "Corp" };
        foreach (var pkg in packages)
            project.BundlePackages.Add(pkg);
        return project;
    }

    [Fact]
    public void Constructor_LoadsPackages()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "Pkg1", SourcePath = "a.msi" },
            new BundlePackageSection { DisplayName = "Pkg2", SourcePath = "b.msi" });

        var vm = new BundlePackagesEditorViewModel(project);
        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal("Pkg1", vm.Entries[0].DisplayName);
        Assert.Equal("Pkg2", vm.Entries[1].DisplayName);
    }

    [Fact]
    public void AddEntry_AddsToCollectionAndModel()
    {
        var project = CreateBundleProject();
        var vm = new BundlePackagesEditorViewModel(project);

        vm.AddEntry();

        Assert.Single(vm.Entries);
        Assert.Single(project.BundlePackages);
        Assert.Equal(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_RemovesEntry()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "Pkg1", SourcePath = "a.msi" });
        var vm = new BundlePackagesEditorViewModel(project);
        vm.SelectedEntry = vm.Entries[0];

        vm.RemoveSelected();

        Assert.Empty(vm.Entries);
        Assert.Empty(project.BundlePackages);
        Assert.Null(vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_NullSelected_NoOp()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "Pkg1", SourcePath = "a.msi" });
        var vm = new BundlePackagesEditorViewModel(project);
        vm.SelectedEntry = null;

        vm.RemoveSelected();

        Assert.Single(vm.Entries);
        Assert.Single(project.BundlePackages);
    }

    [Fact]
    public void EntryViewModel_PropertyChange_UpdatesModel()
    {
        var model = new BundlePackageSection();
        var vm = new BundlePackageEntryViewModel(model);

        vm.Id = "pkg1";
        Assert.Equal("pkg1", model.Id);

        vm.Type = "ExePackage";
        Assert.Equal("ExePackage", model.Type);

        vm.SourcePath = "setup.exe";
        Assert.Equal("setup.exe", model.SourcePath);

        vm.DisplayName = "Setup";
        Assert.Equal("Setup", model.DisplayName);

        vm.Vital = false;
        Assert.False(model.Vital);

        vm.IsPrerequisite = true;
        Assert.True(model.IsPrerequisite);

        vm.DetectionMode = "SearchOnly";
        Assert.Equal("SearchOnly", model.DetectionMode);
    }
}
