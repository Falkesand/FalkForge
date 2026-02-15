namespace FalkForge.Ui.Tests.ViewModels;

using System.Reactive.Linq;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.ViewModels;
using Xunit;

public class MaintenancePageViewModelTests
{
    private readonly TestInstallerEngine _engine = new();

    private DefaultShellViewModel CreateShellWithInstalledState()
    {
        _engine.DetectedState = InstallState.Installed;
        return new DefaultShellViewModel(_engine);
    }

    [Fact]
    public void Title_ReturnsModifySetup()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        Assert.Equal("Modify Setup", vm.Title);
    }

    [Fact]
    public void ProductName_ReturnsManifestName()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        Assert.Equal("TestProduct", vm.ProductName);
    }

    [Fact]
    public void InstalledVersion_WhenInstalled_ReturnsManifestVersion()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        Assert.Equal("1.0.0", vm.InstalledVersion);
    }

    [Fact]
    public void InstalledVersion_WhenNotInstalled_ReturnsEmpty()
    {
        _engine.DetectedState = InstallState.NotInstalled;
        var shell = new DefaultShellViewModel(_engine);
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        Assert.Equal(string.Empty, vm.InstalledVersion);
    }

    [Fact]
    public void CanNavigateNext_ReturnsFalse()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        Assert.False(vm.CanNavigateNext());
    }

    [Fact]
    public void CanNavigateBack_ReturnsFalse()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        Assert.False(vm.CanNavigateBack());
    }

    [Fact]
    public async Task ModifyCommand_CallsPlanAsyncWithModify()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        await vm.ModifyCommand.Execute().FirstAsync();

        Assert.Equal(InstallAction.Modify, _engine.LastPlannedAction);
    }

    [Fact]
    public async Task RepairCommand_CallsPlanAsyncWithRepair()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        await vm.RepairCommand.Execute().FirstAsync();

        Assert.Equal(InstallAction.Repair, _engine.LastPlannedAction);
    }

    [Fact]
    public async Task UninstallCommand_CallsPlanAsyncWithUninstall()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        await vm.UninstallCommand.Execute().FirstAsync();

        Assert.Equal(InstallAction.Uninstall, _engine.LastPlannedAction);
    }

    [Fact]
    public async Task ModifyCommand_NavigatesToProgressPage()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        await vm.ModifyCommand.Execute().FirstAsync();

        Assert.IsType<ProgressPageViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task RepairCommand_NavigatesToProgressPage()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        await vm.RepairCommand.Execute().FirstAsync();

        Assert.IsType<ProgressPageViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task UninstallCommand_NavigatesToProgressPage()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        await vm.UninstallCommand.Execute().FirstAsync();

        Assert.IsType<ProgressPageViewModel>(shell.CurrentPage);
    }

    [Fact]
    public void Description_ContainsProductName()
    {
        var shell = CreateShellWithInstalledState();
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        Assert.Contains("TestProduct", vm.Description);
    }

    [Fact]
    public async Task InitializeAsync_WhenInstalled_NavigatesToMaintenancePage()
    {
        _engine.DetectedState = InstallState.Installed;
        var shell = new DefaultShellViewModel(_engine);

        await shell.InitializeAsync();

        Assert.IsType<MaintenancePageViewModel>(shell.CurrentPage);
        Assert.True(shell.IsMaintenanceMode);
    }

    [Fact]
    public async Task InitializeAsync_WhenNotInstalled_StaysOnWelcomePage()
    {
        _engine.DetectedState = InstallState.NotInstalled;
        var shell = new DefaultShellViewModel(_engine);

        await shell.InitializeAsync();

        Assert.IsType<WelcomePageViewModel>(shell.CurrentPage);
        Assert.False(shell.IsMaintenanceMode);
    }

    [Fact]
    public async Task InitializeAsync_WhenOlderVersion_NavigatesToMaintenancePage()
    {
        _engine.DetectedState = InstallState.OlderVersion;
        var shell = new DefaultShellViewModel(_engine);

        await shell.InitializeAsync();

        Assert.IsType<MaintenancePageViewModel>(shell.CurrentPage);
        Assert.True(shell.IsMaintenanceMode);
    }

    [Fact]
    public async Task InitializeAsync_WhenNewerVersion_NavigatesToMaintenancePage()
    {
        _engine.DetectedState = InstallState.NewerVersion;
        var shell = new DefaultShellViewModel(_engine);

        await shell.InitializeAsync();

        Assert.IsType<MaintenancePageViewModel>(shell.CurrentPage);
        Assert.True(shell.IsMaintenanceMode);
    }
}
