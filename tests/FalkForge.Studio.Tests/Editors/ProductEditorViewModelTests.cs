using FalkForge.Studio.Editors.ProductEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class ProductEditorViewModelTests
{
    private static ProductEditorViewModel CreateVm(ProductSection? model = null, StudioProject? project = null)
    {
        project ??= new StudioProject();
        model ??= project.Product;
        return new ProductEditorViewModel(model, project);
    }

    [Fact]
    public void Properties_ReadFromModel()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "1.0.0" };
        var vm = CreateVm(model);
        Assert.Equal("Test", vm.Name);
        Assert.Equal("Corp", vm.Manufacturer);
        Assert.Equal("1.0.0", vm.Version);
    }

    [Fact]
    public void SetName_UpdatesModel()
    {
        var project = new StudioProject();
        var vm = CreateVm(project: project);
        vm.Name = "Updated";
        Assert.Equal("Updated", project.Product.Name);
    }

    [Fact]
    public void SetName_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProductEditorViewModel.Name))
                raised = true;
        };
        vm.Name = "New";
        Assert.True(raised);
    }

    [Fact]
    public void ValidationError_MissingName_ReturnsError()
    {
        var model = new ProductSection { Name = "", Manufacturer = "Corp" };
        var vm = CreateVm(model);
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("name", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationError_InvalidVersion_ReturnsError()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "not.a.version" };
        var vm = CreateVm(model);
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("version", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationError_ValidFields_ReturnsNull()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "1.0.0" };
        var vm = CreateVm(model);
        Assert.Null(vm.ValidationError);
    }

    [Fact]
    public void ValidationError_InvalidGuid_ReturnsError()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "1.0.0", UpgradeCode = "not-a-guid" };
        var vm = CreateVm(model);
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("GUID", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectType_ReadsFromStudioProject()
    {
        var project = new StudioProject { ProjectType = "bundle" };
        var vm = CreateVm(project: project);
        Assert.Equal("bundle", vm.ProjectType);
    }

    [Fact]
    public void ProjectType_Set_UpdatesStudioProject()
    {
        var project = new StudioProject { ProjectType = "msi" };
        var vm = CreateVm(project: project);
        vm.ProjectType = "msix";
        Assert.Equal("msix", project.ProjectType);
    }

    [Fact]
    public void ProjectType_Set_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProductEditorViewModel.ProjectType))
                raised = true;
        };
        vm.ProjectType = "bundle";
        Assert.True(raised);
    }

    [Fact]
    public void ProjectType_Set_RaisesProjectTypeChangedEvent()
    {
        var vm = CreateVm();
        var raised = false;
        vm.ProjectTypeChanged += (_, _) => raised = true;
        vm.ProjectType = "bundle";
        Assert.True(raised);
    }

    [Fact]
    public void ProjectType_SetSameValue_DoesNotRaiseEvent()
    {
        var project = new StudioProject { ProjectType = "msi" };
        var vm = CreateVm(project: project);
        var raised = false;
        vm.ProjectTypeChanged += (_, _) => raised = true;
        vm.ProjectType = "msi";
        Assert.False(raised);
    }

    [Fact]
    public void SelectedProjectType_MapsFromProjectType()
    {
        var project = new StudioProject { ProjectType = "bundle" };
        var vm = CreateVm(project: project);
        Assert.Equal("bundle", vm.SelectedProjectType.Value);
        Assert.Equal("EXE Bundle", vm.SelectedProjectType.DisplayName);
    }

    [Fact]
    public void SelectedProjectType_Set_UpdatesProjectType()
    {
        var vm = CreateVm();
        vm.SelectedProjectType = new ProjectTypeItem("MSIX Package", "msix");
        Assert.Equal("msix", vm.ProjectType);
    }
}
