using FalkForge.Studio.Editors.BuildSettingsEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class BuildSettingsEditorViewModelTests
{
    [Fact]
    public void OutputPath_ReadsFromModel()
    {
        var model = new BuildSection { OutputPath = "dist/" };
        var vm = new BuildSettingsEditorViewModel(model);
        Assert.Equal("dist/", vm.OutputPath);
    }

    [Fact]
    public void Compression_Set_UpdatesModel()
    {
        var model = new BuildSection();
        var vm = new BuildSettingsEditorViewModel(model);
        vm.Compression = "Low";
        Assert.Equal("Low", model.Compression);
    }

    [Fact]
    public void CompressionLevels_ContainsFourValues()
    {
        var vm = new BuildSettingsEditorViewModel(new BuildSection());
        Assert.Equal(4, vm.CompressionLevels.Length);
    }
}
