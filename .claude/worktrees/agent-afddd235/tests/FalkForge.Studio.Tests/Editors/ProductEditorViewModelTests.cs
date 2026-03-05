using FalkForge.Studio.Editors.ProductEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class ProductEditorViewModelTests
{
    [Fact]
    public void Properties_ReadFromModel()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "1.0.0" };
        var vm = new ProductEditorViewModel(model);
        Assert.Equal("Test", vm.Name);
        Assert.Equal("Corp", vm.Manufacturer);
        Assert.Equal("1.0.0", vm.Version);
    }

    [Fact]
    public void SetName_UpdatesModel()
    {
        var model = new ProductSection();
        var vm = new ProductEditorViewModel(model);
        vm.Name = "Updated";
        Assert.Equal("Updated", model.Name);
    }

    [Fact]
    public void SetName_RaisesPropertyChanged()
    {
        var model = new ProductSection();
        var vm = new ProductEditorViewModel(model);
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
        var vm = new ProductEditorViewModel(model);
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("name", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationError_InvalidVersion_ReturnsError()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "not.a.version" };
        var vm = new ProductEditorViewModel(model);
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("version", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationError_ValidFields_ReturnsNull()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "1.0.0" };
        var vm = new ProductEditorViewModel(model);
        Assert.Null(vm.ValidationError);
    }

    [Fact]
    public void ValidationError_InvalidGuid_ReturnsError()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "1.0.0", UpgradeCode = "not-a-guid" };
        var vm = new ProductEditorViewModel(model);
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("GUID", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }
}
