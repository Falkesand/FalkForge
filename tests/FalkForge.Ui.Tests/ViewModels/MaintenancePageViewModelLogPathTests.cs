namespace FalkForge.Ui.Tests.ViewModels;

using System.Linq;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.ViewModels;
using Xunit;

public class MaintenancePageViewModelLogPathTests
{
    private static MaintenancePageViewModel Create(TestInstallerEngine engine)
    {
        engine.DetectedState = InstallState.Installed;
        var shell = new DefaultShellViewModel(engine);
        return shell.Pages.OfType<MaintenancePageViewModel>().Single();
    }

    [Fact]
    public void HasLogPath_IsFalse_WhenLogPathNull()
    {
        var engine = new TestInstallerEngine { LogPath = null };
        var vm = Create(engine);

        Assert.False(vm.HasLogPath);
        Assert.Null(vm.LogPath);
    }

    [Fact]
    public void HasLogPath_IsTrue_WhenLogPathProvided()
    {
        var engine = new TestInstallerEngine { LogPath = @"C:\Temp\session.log" };
        var vm = Create(engine);

        Assert.True(vm.HasLogPath);
        Assert.Equal(@"C:\Temp\session.log", vm.LogPath);
    }

    [Fact]
    public void OpenLogCommand_CannotExecute_WhenFileMissing()
    {
        var engine = new TestInstallerEngine { LogPath = @"C:\Does\Not\Exist\nope.log" };
        var vm = Create(engine);

        Assert.False(vm.OpenLogCommand.CanExecute(null));
    }
}
