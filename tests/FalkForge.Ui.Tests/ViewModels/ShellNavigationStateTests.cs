namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// The wizard's Next and Back buttons bind straight to <c>CanGoNext</c> / <c>CanGoBack</c> on the
/// shell (<c>MainWindow.xaml</c>), and both properties delegate to the CURRENT PAGE's
/// <c>CanNavigateNext()</c> / <c>CanNavigateBack()</c>. So whenever a page changes the state those
/// methods read, the shell has to re-raise its own properties or the button never updates.
/// <para>
/// Measured before the fix, driving the real wizard with UI Automation: on the licence page the
/// accept checkbox reported <c>ToggleState=On</c> while <c>Next &gt;</c> reported
/// <c>IsEnabled=False</c>. The shell raised <c>CanGoNext</c> only from
/// <c>OnCurrentPageChanged</c>, which runs on navigation and never again, so ticking the box moved
/// nothing. The buttons are plain <c>IsEnabled</c> bindings rather than commands, so
/// <c>CommandManager.InvalidateRequerySuggested</c> could not cover for it either. The wizard was
/// unusable from the licence page onwards.
/// </para>
/// </summary>
public class ShellNavigationStateTests
{
    private readonly TestInstallerEngine _engine = new();

    [Fact]
    public async Task AcceptingTheLicence_RaisesCanGoNext()
    {
        var shell = new DefaultShellViewModel(_engine);
        await shell.NavigateNext();
        var licence = Assert.IsType<LicensePageViewModel>(shell.CurrentPage);
        Assert.False(shell.CanGoNext);

        var changed = new List<string?>();
        shell.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        licence.IsAccepted = true;

        Assert.Contains(nameof(shell.CanGoNext), changed);
        Assert.Contains(nameof(shell.CanGoBack), changed);
        Assert.True(shell.CanGoNext);
    }

    [Fact]
    public async Task PageLeftBehind_NoLongerDrivesTheShell()
    {
        // A page the wizard has moved past must not keep re-raising the shell's navigation
        // state; otherwise a late notification from an abandoned page overwrites the state of
        // the page the user is actually looking at.
        var shell = new DefaultShellViewModel(_engine);
        await shell.NavigateNext();
        var licence = Assert.IsType<LicensePageViewModel>(shell.CurrentPage);
        licence.IsAccepted = true;
        await shell.NavigateNext();

        var changed = new List<string?>();
        shell.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        licence.IsAccepted = false;

        Assert.Empty(changed);
    }
}
