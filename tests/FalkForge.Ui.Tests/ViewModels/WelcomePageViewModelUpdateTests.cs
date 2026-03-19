namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using FalkForge.Ui.ViewModels;
using Xunit;

public class WelcomePageViewModelUpdateTests
{
    private readonly TestInstallerEngine _engine = new();

    private WelcomePageViewModel CreateViewModel(TestNavigationService? navigation = null)
    {
        navigation ??= new TestNavigationService();
        return new WelcomePageViewModel(_engine, navigation);
    }

    [Fact]
    public void IsDownloadingUpdate_DefaultsFalse()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsDownloadingUpdate);
    }

    [Fact]
    public void DownloadPercent_DefaultsToZero()
    {
        var vm = CreateViewModel();

        Assert.Equal(0, vm.DownloadPercent);
    }

    [Fact]
    public void UpdateDownloadProgress_SetsDownloadPercent()
    {
        var vm = CreateViewModel();

        vm.UpdateDownloadProgress(42, 420, 1000);

        Assert.Equal(42, vm.DownloadPercent);
    }

    [Fact]
    public void UpdateDownloadProgress_SetsIsDownloadingUpdateTrue()
    {
        var vm = CreateViewModel();

        vm.UpdateDownloadProgress(10, 100, 1000);

        Assert.True(vm.IsDownloadingUpdate);
    }

    [Fact]
    public void UpdateDownloadProgress_RaisesPropertyChanged_ForDownloadPercent()
    {
        var vm = CreateViewModel();
        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        vm.UpdateDownloadProgress(50, 500, 1000);

        Assert.Contains(nameof(WelcomePageViewModel.DownloadPercent), changedProperties);
    }

    [Fact]
    public void UpdateDownloadProgress_RaisesPropertyChanged_ForIsDownloadingUpdate()
    {
        var vm = CreateViewModel();
        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        vm.UpdateDownloadProgress(50, 500, 1000);

        Assert.Contains(nameof(WelcomePageViewModel.IsDownloadingUpdate), changedProperties);
    }

    [Fact]
    public void UpdateDownloadProgress_CalledMultipleTimes_UpdatesPercent()
    {
        var vm = CreateViewModel();

        vm.UpdateDownloadProgress(25, 250, 1000);
        Assert.Equal(25, vm.DownloadPercent);

        vm.UpdateDownloadProgress(75, 750, 1000);
        Assert.Equal(75, vm.DownloadPercent);
    }

    [Fact]
    public void UpdateDownloadProgress_WithSamePercent_DoesNotRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        vm.UpdateDownloadProgress(50, 500, 1000);

        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        vm.UpdateDownloadProgress(50, 500, 1000);

        Assert.DoesNotContain(nameof(WelcomePageViewModel.DownloadPercent), changedProperties);
    }

    [Fact]
    public void UpdateDownloadProgress_At100Percent_SetsIsDownloadingUpdateFalse()
    {
        var vm = CreateViewModel();

        vm.UpdateDownloadProgress(100, 1000, 1000);

        Assert.False(vm.IsDownloadingUpdate);
        Assert.Equal(100, vm.DownloadPercent);
    }

    [Fact]
    public void UpdateDownloadProgress_TransitionTo100_RaisesIsDownloadingUpdateChanged()
    {
        var vm = CreateViewModel();
        vm.UpdateDownloadProgress(50, 500, 1000);
        Assert.True(vm.IsDownloadingUpdate);

        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        vm.UpdateDownloadProgress(100, 1000, 1000);

        Assert.Contains(nameof(WelcomePageViewModel.IsDownloadingUpdate), changedProperties);
        Assert.False(vm.IsDownloadingUpdate);
    }

    [Fact]
    public void UpdateDownloadProgress_ZeroPercent_SetsIsDownloadingUpdateTrue()
    {
        var vm = CreateViewModel();

        vm.UpdateDownloadProgress(0, 0, 1000);

        Assert.True(vm.IsDownloadingUpdate);
    }

    private sealed class TestNavigationService : INavigationService
    {
        public InstallerPageViewModel? CurrentPage { get; set; }
        public bool CanGoBack => false;
        public bool CanGoNext => false;
        public IReadOnlyList<InstallerPageViewModel> Pages { get; } = [];
        public void NavigateNext() { }
        public void NavigateBack() { }
        public void NavigateTo(InstallerPageViewModel page) { CurrentPage = page; }
        public void NavigateTo<T>() where T : InstallerPageViewModel { }
    }
}
