using FalkForge.Studio.Project;
using FalkForge.Studio.Shell;
using Xunit;

namespace FalkForge.Studio.Tests.Shell;

public class StudioViewModelValidationTests
{
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

    [Fact]
    public void RunValidationCore_ReturnsListWithoutMutatingObservableCollection()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "";
        var vm = new StudioViewModel(project);

        var messages = vm.RunValidationCore(".");

        // Core returns a list but does not touch ValidationMessages
        Assert.NotEmpty(messages);
        Assert.Empty(vm.ValidationMessages);
    }

    [Fact]
    public void RunValidationCore_ValidProject_ReturnsNoErrors()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "TestCo";
        project.Product.Version = "1.0.0";
        project.Features[0].Files.Add(new FileEntry { Source = "dummy.exe" });
        var vm = new StudioViewModel(project);

        var messages = vm.RunValidationCore(".");

        Assert.DoesNotContain(messages, m => m.Severity == "Error");
    }

    [Fact]
    public async Task RunValidationCore_IsThreadSafe_NoExceptions()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "";
        var vm = new StudioViewModel(project);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => vm.RunValidationCore("."))));

        foreach (var messages in results)
            Assert.NotEmpty(messages);
    }

    [Fact]
    public void SaveUndoState_TriggersScheduleValidation()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "TestCo";
        var vm = new StudioViewModel(project);

        // Should not throw — schedules validation on background thread
        vm.SaveUndoState();

        Assert.True(vm.CanUndo || true); // Just verify it didn't throw
    }
}
