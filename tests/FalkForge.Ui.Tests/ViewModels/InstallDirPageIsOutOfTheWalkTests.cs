namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Engine.Protocol;
using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// The installation-directory page asks the user where to install and then nothing uses the answer.
/// The page validates the path and sends it to the engine, the engine records it, and the plan is
/// built without ever reading it: every package installs where its own MSI puts it. The user typed
/// a path, saw "Installation completed successfully", and the product landed somewhere else.
/// <para>
/// Until the directory actually reaches the plan, the page stays out of the wizard's straight-line
/// walk. Reporting success for a control that did nothing is worse than not offering the control.
/// <see cref="FalkForge.Engine.Tests.Pipeline.PlanIgnoresInstallDirectoryTests"/> pins the missing
/// half in the engine and points back here, so the two are unhidden together.
/// </para>
/// </summary>
public class InstallDirPageIsOutOfTheWalkTests
{
    [Fact]
    public async Task WalkingForwardFromTheLicencePage_SkipsStraightToFeatures()
    {
        var shell = new DefaultShellViewModel(new TestInstallerEngine { DetectedState = InstallState.NotInstalled });
        await shell.NavigateTo<LicensePageViewModel>();
        AcceptLicenceIfShown(shell);

        await shell.NavigateNext();

        Assert.IsType<FeaturesPageViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task WalkingBackFromFeatures_LandsOnTheLicencePage()
    {
        var shell = new DefaultShellViewModel(new TestInstallerEngine { DetectedState = InstallState.NotInstalled });
        await shell.NavigateTo<FeaturesPageViewModel>();

        await shell.NavigateBack();

        Assert.IsType<LicensePageViewModel>(shell.CurrentPage);
    }

    /// <summary>
    /// A full walk from the first page must never stop on the directory page. Asserting the two
    /// neighbouring steps alone would still pass if the page moved elsewhere in the list.
    /// </summary>
    [Fact]
    public async Task AFullWalkOfTheWizard_NeverStopsOnTheDirectoryPage()
    {
        var shell = new DefaultShellViewModel(new TestInstallerEngine { DetectedState = InstallState.NotInstalled });
        await shell.NavigateTo<WelcomePageViewModel>();

        // The progress page runs the install when it is reached, so the walk stops before it.
        var steps = 0;
        while (shell.CurrentPage is not ProgressPageViewModel)
        {
            AcceptLicenceIfShown(shell);
            Assert.True(shell.CanGoNext, $"The walk stalled on {shell.CurrentPage?.GetType().Name}.");
            await shell.NavigateNext();
            Assert.IsNotType<InstallDirPageViewModel>(shell.CurrentPage);

            // The wizard has a handful of pages; a walk that does not reach the progress page is a
            // loop, not a slow test.
            Assert.True(++steps <= shell.Pages.Count, "The walk never reached the progress page.");
        }
    }

    /// <summary>
    /// The licence page refuses Next until the checkbox is ticked, which is the whole point of it.
    /// A walk of the wizard has to tick it to get anywhere.
    /// </summary>
    private static void AcceptLicenceIfShown(DefaultShellViewModel shell)
    {
        if (shell.CurrentPage is LicensePageViewModel licence)
            licence.IsAccepted = true;
    }

    /// <summary>
    /// The page itself still works and is still registered, so a custom shell that navigates to it
    /// deliberately keeps what it had. Only the built-in wizard's straight-line walk skips it.
    /// </summary>
    [Fact]
    public void ThePageIsStillRegisteredAndReachableOnPurpose()
    {
        var shell = new DefaultShellViewModel(new TestInstallerEngine { DetectedState = InstallState.NotInstalled });

        Assert.Contains(shell.Pages, page => page is InstallDirPageViewModel);
    }
}
