namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using FalkForge.Ui.ViewModels;
using Xunit;

public class UpdateAvailablePageViewModelTests
{
    private readonly TestInstallerEngine _engine = new();

    private UpdateAvailablePageViewModel CreateViewModel(TestNavigationService? navigation = null)
    {
        navigation ??= new TestNavigationService();
        return new UpdateAvailablePageViewModel(_engine, navigation);
    }

    [Fact]
    public void Title_ReturnsUpdateAvailable()
    {
        var vm = CreateViewModel();

        Assert.Equal("Update Available", vm.Title);
    }

    [Fact]
    public void Description_ContainsProductName()
    {
        var vm = CreateViewModel();

        Assert.Contains("TestProduct", vm.Description);
    }

    [Fact]
    public void SetUpdateInfo_SetsAllProperties()
    {
        var vm = CreateViewModel();

        vm.SetUpdateInfo("2.0.0", @"C:\cache\update.exe", 1024 * 1024);

        Assert.Equal("2.0.0", vm.UpdateVersion);
        Assert.Equal(@"C:\cache\update.exe", vm.CachedFilePath);
        Assert.Equal(1024 * 1024, vm.UpdateSize);
    }

    [Fact]
    public void SetUpdateInfo_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        vm.SetUpdateInfo("2.0.0", @"C:\cache\update.exe", 2048);

        Assert.Contains(nameof(UpdateAvailablePageViewModel.UpdateVersion), changedProperties);
        Assert.Contains(nameof(UpdateAvailablePageViewModel.CachedFilePath), changedProperties);
        Assert.Contains(nameof(UpdateAvailablePageViewModel.UpdateSize), changedProperties);
    }

    [Fact]
    public void CanNavigateBack_ReturnsTrue()
    {
        var vm = CreateViewModel();

        Assert.True(vm.CanNavigateBack());
    }

    [Fact]
    public void CanNavigateNext_ReturnsTrue()
    {
        var vm = CreateViewModel();

        Assert.True(vm.CanNavigateNext());
    }

    [Fact]
    public void UpdateNowCommand_CallsLaunchUpdate()
    {
        _engine.LaunchUpdateCalled = false;
        var vm = CreateViewModel();

        vm.UpdateNowCommand.Execute(null);

        Assert.True(_engine.LaunchUpdateCalled);
    }

    [Fact]
    public void Description_AfterSetUpdateInfo_ContainsVersion()
    {
        var vm = CreateViewModel();

        vm.SetUpdateInfo("3.5.0", null, 0);

        Assert.Contains("3.5.0", vm.Description);
    }

    [Fact]
    public void LaterCommand_NavigatesNext()
    {
        var navigation = new TestNavigationService();
        var vm = CreateViewModel(navigation);

        vm.LaterCommand.Execute(null);

        Assert.True(navigation.NavigateNextCalled);
    }

    [Fact]
    public void UpdateNowCommand_CallsShutdownAfterLaunchUpdate()
    {
        _engine.LaunchUpdateCalled = false;
        var vm = CreateViewModel();

        vm.UpdateNowCommand.Execute(null);

        Assert.True(_engine.LaunchUpdateCalled);
        Assert.True(_engine.ShutdownCalled);
    }

    [Fact]
    public void InitialState_HasNullValues()
    {
        var vm = CreateViewModel();

        Assert.Null(vm.UpdateVersion);
        Assert.Null(vm.CachedFilePath);
        Assert.Null(vm.ReleaseNotes);
        Assert.Equal(0L, vm.UpdateSize);
    }

    [Fact]
    public void SetUpdateInfo_WithReleaseNotes_SetsReleaseNotes()
    {
        var vm = CreateViewModel();

        vm.SetUpdateInfo("2.0.0", null, 0, "Fixed critical security issue");

        Assert.Equal("Fixed critical security issue", vm.ReleaseNotes);
    }

    [Fact]
    public void SetUpdateInfo_RaisesPropertyChanged_ForReleaseNotes()
    {
        var vm = CreateViewModel();
        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        vm.SetUpdateInfo("2.0.0", null, 0, "New features");

        Assert.Contains(nameof(UpdateAvailablePageViewModel.ReleaseNotes), changedProperties);
    }

    [Fact]
    public void SetUpdateInfo_RaisesPropertyChanged_ForDescription()
    {
        var vm = CreateViewModel();
        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        vm.SetUpdateInfo("2.0.0", null, 0);

        Assert.Contains(nameof(UpdateAvailablePageViewModel.Description), changedProperties);
    }

    [Fact]
    public void SetUpdateInfo_SameVersion_DoesNotRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        vm.SetUpdateInfo("2.0.0", null, 1024);

        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        vm.SetUpdateInfo("2.0.0", null, 1024);

        Assert.DoesNotContain(nameof(UpdateAvailablePageViewModel.UpdateVersion), changedProperties);
        Assert.DoesNotContain(nameof(UpdateAvailablePageViewModel.UpdateSize), changedProperties);
    }

    [Fact]
    public void Description_WithoutVersion_ContainsProductName()
    {
        var vm = CreateViewModel();

        Assert.Contains("TestProduct", vm.Description);
        Assert.DoesNotContain("Version", vm.Description);
    }

    [Fact]
    public void SetUpdateInfo_NullReleaseNotes_LeavesNull()
    {
        var vm = CreateViewModel();

        vm.SetUpdateInfo("2.0.0", @"C:\cache\update.exe", 2048, null);

        Assert.Null(vm.ReleaseNotes);
    }

    private sealed class TestNavigationService : INavigationService
    {
        public InstallerPageViewModel? CurrentPage { get; set; }
        public bool CanGoBack => false;
        public bool CanGoNext => false;
        public IReadOnlyList<InstallerPageViewModel> Pages { get; } = [];
        public bool NavigateNextCalled { get; private set; }
        public void NavigateNext() { NavigateNextCalled = true; }
        public void NavigateBack() { }
        public void NavigateTo(InstallerPageViewModel page) { CurrentPage = page; }
        public void NavigateTo<T>() where T : InstallerPageViewModel { }
    }
}
