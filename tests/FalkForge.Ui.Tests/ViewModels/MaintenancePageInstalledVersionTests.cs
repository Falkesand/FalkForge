namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Engine.Protocol;
using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// Verifies that MaintenancePageViewModel.InstalledVersion reflects the actual
/// installed product version detected by the engine, not the new bundle version.
/// </summary>
public class MaintenancePageInstalledVersionTests
{
    [Fact]
    public void InstalledVersion_WhenInstalled_ReturnsInstalledProductVersion_NotManifestVersion()
    {
        // Arrange: installed version differs from the manifest/bundle version.
        var engine = new TestInstallerEngine
        {
            DetectedState = InstallState.Installed,
            InstalledProductVersion = "2.3.4"
            // Manifest.Version is "1.0.0" (set in TestInstallerEngine ctor)
        };
        var shell = new DefaultShellViewModel(engine);
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        // Act + Assert: must report what's ON disk, not what the new bundle says.
        Assert.Equal("2.3.4", vm.InstalledVersion);
        Assert.NotEqual(engine.Manifest.Version, vm.InstalledVersion);
    }

    [Fact]
    public void InstalledVersion_WhenOlderVersion_ReturnsInstalledProductVersion()
    {
        var engine = new TestInstallerEngine
        {
            DetectedState = InstallState.OlderVersion,
            InstalledProductVersion = "0.9.0"
        };
        var shell = new DefaultShellViewModel(engine);
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        Assert.Equal("0.9.0", vm.InstalledVersion);
    }

    [Fact]
    public void InstalledVersion_WhenNotInstalled_ReturnsEmpty()
    {
        var engine = new TestInstallerEngine
        {
            DetectedState = InstallState.NotInstalled,
            InstalledProductVersion = null
        };
        var shell = new DefaultShellViewModel(engine);
        var vm = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        Assert.Equal(string.Empty, vm.InstalledVersion);
    }
}
