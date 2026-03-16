using FalkForge.Studio.Project;
using FalkForge.Studio.Shell;
using Xunit;

namespace FalkForge.Studio.Tests.Shell;

public class StudioViewModelValidationTests
{
    private static StudioViewModel CreateViewModel(Action<StudioProject>? configure = null)
    {
        var vm = new StudioViewModel();
        // Access internal project via NewProject + LoadProject would reset state,
        // so we build by modifying the default project through the view model's public API.
        // Instead, we create a fresh VM and manipulate the project before validation.
        return vm;
    }

    [Fact]
    public void RunValidation_EmptyName_AddsError()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "";
        var vm = new StudioViewModel(project);

        vm.RunValidation();

        Assert.Contains(vm.ValidationMessages, m => m.Code == "STU001" && m.Severity == "Error");
    }

    [Fact]
    public void RunValidation_EmptyManufacturer_AddsError()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Manufacturer = "";
        var vm = new StudioViewModel(project);

        vm.RunValidation();

        Assert.Contains(vm.ValidationMessages, m => m.Code == "STU002" && m.Severity == "Error");
    }

    [Fact]
    public void RunValidation_InvalidVersion_AddsError()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "TestCo";
        project.Product.Version = "not-a-version";
        var vm = new StudioViewModel(project);

        vm.RunValidation();

        Assert.Contains(vm.ValidationMessages, m => m.Code == "STU003" && m.Severity == "Error");
    }

    [Fact]
    public void RunValidation_NoFeatures_AddsError()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "TestCo";
        project.Features.Clear();
        var vm = new StudioViewModel(project);

        vm.RunValidation();

        Assert.Contains(vm.ValidationMessages, m => m.Code == "STU004" && m.Severity == "Error");
    }

    [Fact]
    public void RunValidation_ValidProject_NoErrors()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "TestCo";
        project.Product.Version = "1.0.0";
        project.Features[0].Files.Add(new FileEntry { Source = "dummy.exe" });
        var vm = new StudioViewModel(project);

        vm.RunValidation();

        Assert.DoesNotContain(vm.ValidationMessages, m => m.Severity == "Error");
    }

    [Fact]
    public void RunValidation_FeatureWithNoFiles_AddsWarning()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "TestCo";
        // Default feature has no files
        var vm = new StudioViewModel(project);

        vm.RunValidation();

        Assert.Contains(vm.ValidationMessages, m => m.Code == "STU005" && m.Severity == "Warning");
    }

    [Fact]
    public void RunValidation_InvalidUpgradeCode_AddsError()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "TestCo";
        project.Product.UpgradeCode = "not-a-guid";
        var vm = new StudioViewModel(project);

        vm.RunValidation();

        Assert.Contains(vm.ValidationMessages, m => m.Code == "STU006" && m.Severity == "Error");
    }

    [Fact]
    public void ErrorCount_ReflectsValidationMessages()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "";
        project.Product.Manufacturer = "";
        var vm = new StudioViewModel(project);

        vm.RunValidation();

        Assert.True(vm.ErrorCount >= 2);
    }
}
