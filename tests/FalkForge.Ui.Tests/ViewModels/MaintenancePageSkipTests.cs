namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Engine.Protocol;
using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// The maintenance page sits at index 4 of the default wizard, between Features and Progress, and
/// its <c>CanNavigateNext</c> and <c>CanNavigateBack</c> both return false: the user is meant to
/// pick Modify, Repair or Uninstall on it rather than walk past it.
/// <para>
/// On a machine where the product is NOT installed that page is a dead end. A fresh install walked
/// Welcome, Licence, Directory, Features and then stopped on a page headed "Modify Setup" that
/// says the product is already installed, with Next and Back both greyed out and only Cancel left.
/// The shell only ever jumps to that page deliberately, from <c>InitializeAsync</c>, when detection
/// reports the product present.
/// </para>
/// </summary>
public class MaintenancePageSkipTests
{
    [Fact]
    public async Task FreshInstall_WalksFromFeaturesToProgress()
    {
        var shell = new DefaultShellViewModel(new TestInstallerEngine { DetectedState = InstallState.NotInstalled });
        await shell.NavigateTo<FeaturesPageViewModel>();

        Assert.True(shell.CanGoNext);
        await shell.NavigateNext();

        Assert.IsType<ProgressPageViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task FreshInstall_StillWalksBackwardsThroughTheEarlierPages()
    {
        var shell = new DefaultShellViewModel(new TestInstallerEngine { DetectedState = InstallState.NotInstalled });
        await shell.NavigateTo<FeaturesPageViewModel>();

        Assert.True(shell.CanGoBack);
        await shell.NavigateBack();

        Assert.IsType<InstallDirPageViewModel>(shell.CurrentPage);
    }

    [Theory]
    [InlineData(InstallState.Installed)]
    [InlineData(InstallState.OlderVersion)]
    [InlineData(InstallState.NewerVersion)]
    public async Task InstalledProduct_StillStopsOnTheMaintenancePage(InstallState state)
    {
        var shell = new DefaultShellViewModel(new TestInstallerEngine { DetectedState = state });
        await shell.NavigateTo<FeaturesPageViewModel>();

        await shell.NavigateNext();

        Assert.IsType<MaintenancePageViewModel>(shell.CurrentPage);
        Assert.False(shell.CanGoNext);
    }
}
