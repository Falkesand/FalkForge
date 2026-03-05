using FalkForge.Studio.Editors.FeaturesEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class FeaturesEditorViewModelTests
{
    [Fact]
    public void Constructor_LoadsFeatures()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new FeaturesEditorViewModel(project);
        Assert.Single(vm.Features);
        Assert.Equal("Main", vm.Features[0].Id);
    }

    [Fact]
    public void AddFeature_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new FeaturesEditorViewModel(project);
        vm.AddFeature("Extras", "Extra Components");
        Assert.Equal(2, vm.Features.Count);
        Assert.Equal(2, project.Features.Count);
    }

    [Fact]
    public void RemoveSelected_RemovesFeature()
    {
        var project = StudioProjectLoader.NewProject();
        project.Features.Add(new FeatureSection { Id = "Second", Title = "Second" });
        var vm = new FeaturesEditorViewModel(project);
        vm.SelectedFeature = vm.Features[1];
        vm.RemoveSelected();
        Assert.Single(vm.Features);
    }

    [Fact]
    public void RemoveSelected_CannotRemoveLastFeature()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new FeaturesEditorViewModel(project);
        vm.SelectedFeature = vm.Features[0];
        vm.RemoveSelected();
        Assert.Single(vm.Features);
    }

    [Fact]
    public void FeatureNode_PropertyChange_UpdatesModel()
    {
        var model = new FeatureSection { Id = "Test", Title = "Test" };
        var vm = new FeatureNodeViewModel(model);
        vm.Title = "Updated";
        Assert.Equal("Updated", model.Title);
    }

    [Fact]
    public void AddFeature_SetsSelected()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new FeaturesEditorViewModel(project);
        vm.AddFeature("New", "New Feature");
        Assert.Equal("New", vm.SelectedFeature?.Id);
    }
}
