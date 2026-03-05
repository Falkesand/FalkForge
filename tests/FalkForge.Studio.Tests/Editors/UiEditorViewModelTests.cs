using FalkForge.Studio.Editors.UiEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class UiEditorViewModelTests
{
    [Fact]
    public void DialogSet_ReadsFromModel()
    {
        var model = new UiSection { DialogSet = "FeatureTree" };
        var vm = new UiEditorViewModel(model);
        Assert.Equal("FeatureTree", vm.DialogSet);
    }

    [Fact]
    public void DialogSet_Set_UpdatesModel()
    {
        var model = new UiSection();
        var vm = new UiEditorViewModel(model);
        vm.DialogSet = "Mondo";
        Assert.Equal("Mondo", model.DialogSet);
    }

    [Fact]
    public void LicenseFile_Set_UpdatesModel()
    {
        var model = new UiSection();
        var vm = new UiEditorViewModel(model);
        vm.LicenseFile = "license.rtf";
        Assert.Equal("license.rtf", model.LicenseFile);
    }

    [Fact]
    public void DialogSets_ContainsAll()
    {
        var vm = new UiEditorViewModel(new UiSection());
        Assert.Equal(6, vm.DialogSets.Length);
        Assert.Contains("FeatureTree", vm.DialogSets);
    }
}
