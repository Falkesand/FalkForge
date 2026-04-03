using System.IO;
using FalkForge.Studio.Project;
using FalkForge.Studio.Shell;
using Xunit;

namespace FalkForge.Studio.Tests.Shell;

public class StudioViewModelBuildTests
{
    [Fact]
    public async Task BuildAsync_SetsIsBuildInProgress_True()
    {
        var vm = new StudioViewModel();
        var wasTrue = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StudioViewModel.IsBuildInProgress) && vm.IsBuildInProgress)
                wasTrue = true;
        };

        await vm.BuildAsync(".");

        Assert.True(wasTrue, "IsBuildInProgress should have been set to true during build.");
    }

    [Fact]
    public async Task BuildAsync_OnComplete_SetsIsBuildInProgress_False()
    {
        var vm = new StudioViewModel();

        await vm.BuildAsync(".");

        Assert.False(vm.IsBuildInProgress);
    }

    [Fact]
    public async Task BuildAsync_OutputContainsTimestampedLines()
    {
        var vm = new StudioViewModel();

        await vm.BuildAsync(".");

        Assert.Contains("Build started", vm.OutputText);
        Assert.Matches(@"\[\d{2}:\d{2}:\d{2}\]", vm.OutputText);
    }

    [Fact]
    public async Task BuildAsync_SetsBuildSummary()
    {
        var vm = new StudioViewModel();

        await vm.BuildAsync(".");

        Assert.NotNull(vm.BuildSummary);
        Assert.True(vm.HasBuildSummary);
    }

    [Fact]
    public async Task BuildAsync_FailedBuild_BuildSucceededIsFalse()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "";
        var vm = new StudioViewModel(project);

        await vm.BuildAsync(".");

        Assert.False(vm.BuildSucceeded);
        Assert.Contains("failed", vm.BuildSummary!);
    }

    [Fact]
    public async Task BuildAsync_ShowBuildProgress_ResetAfterBuild()
    {
        var vm = new StudioViewModel();

        await vm.BuildAsync(".");

        Assert.False(vm.ShowBuildProgress);
    }

    [Fact]
    public void IsBuildInProgress_DefaultIsFalse()
    {
        var vm = new StudioViewModel();
        Assert.False(vm.IsBuildInProgress);
    }

    [Fact]
    public void IsBuildInProgress_RaisesPropertyChanged()
    {
        var vm = new StudioViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StudioViewModel.IsBuildInProgress))
                raised = true;
        };

        // Trigger build which sets the property
        _ = vm.BuildAsync(".");

        Assert.True(raised, "PropertyChanged should fire for IsBuildInProgress.");
    }
}
