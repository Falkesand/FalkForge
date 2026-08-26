namespace FalkForge.Ui.Tests.ViewModels;

using System.Reactive.Linq;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// The built-in wizard's progress page is where the install actually happens, and until
/// 2026-08-26 it never happened at all. <c>ProgressPageViewModel.OnNavigatedToAsync</c> subscribed
/// to the engine's progress, status and phase streams and then returned. Nothing in
/// <c>DefaultShellViewModel</c> or any page it registers called <c>PlanAsync</c> or
/// <c>ApplyAsync</c> for an install: the only <c>ApplyAsync</c> call site in the whole of
/// <c>src/</c> was <c>CustomShellViewModel</c>, which the built-in UI does not use.
/// <para>
/// Measured by driving the real wizard on a real bundle: the window walked to the progress page,
/// sat at 0%, and the engine never planned or executed the MSI.
/// </para>
/// </summary>
public class ProgressPageInstallRunTests
{
    private static ProgressPageViewModel ProgressPage(DefaultShellViewModel shell)
        => shell.Pages.OfType<ProgressPageViewModel>().Single();

    private static CompletePageViewModel CompletePage(DefaultShellViewModel shell)
        => shell.Pages.OfType<CompletePageViewModel>().Single();

    [Fact]
    public async Task ReachingTheProgressPage_PlansAndAppliesTheInstall()
    {
        var engine = new TestInstallerEngine();
        var shell = new DefaultShellViewModel(engine);

        await shell.NavigateTo<ProgressPageViewModel>();

        Assert.Equal(InstallAction.Install, engine.LastPlannedAction);
        Assert.True(engine.ApplyCalled);
    }

    [Fact]
    public async Task AFinishedInstall_EnablesNextAndReportsSuccess()
    {
        var engine = new TestInstallerEngine();
        var shell = new DefaultShellViewModel(engine);

        await shell.NavigateTo<ProgressPageViewModel>();

        Assert.True(ProgressPage(shell).IsComplete);
        Assert.True(shell.CanGoNext);
        Assert.True(CompletePage(shell).IsSuccess);
    }

    [Fact]
    public async Task ANonZeroExitCode_IsReportedAsAFailure()
    {
        var engine = new TestInstallerEngine { ApplyResult = new FalkForge.Ui.Abstractions.ApplyResult(1603, "Fatal error during installation.") };
        var shell = new DefaultShellViewModel(engine);

        await shell.NavigateTo<ProgressPageViewModel>();

        Assert.True(ProgressPage(shell).IsComplete);
        Assert.False(CompletePage(shell).IsSuccess);
        Assert.Contains("1603", CompletePage(shell).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEngineThatRefusesToPlan_ReportsTheReasonInsteadOfHanging()
    {
        // The engine refuses to plan when, for example, the licence has not been accepted. The
        // wizard has to say so rather than sit on a progress bar that will never move.
        var engine = new TestInstallerEngine
        {
            PlanFailure = new InvalidOperationException("License agreement has not been accepted.")
        };
        var shell = new DefaultShellViewModel(engine);

        await shell.NavigateTo<ProgressPageViewModel>();

        Assert.True(ProgressPage(shell).IsComplete);
        Assert.False(engine.ApplyCalled);
        Assert.False(CompletePage(shell).IsSuccess);
        Assert.Contains("License agreement", CompletePage(shell).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMaintenancePage_RunsTheActionItAskedFor()
    {
        var engine = new TestInstallerEngine { DetectedState = InstallState.Installed };
        var shell = new DefaultShellViewModel(engine);
        var maintenance = shell.Pages.OfType<MaintenancePageViewModel>().Single();

        await maintenance.UninstallCommand.Execute().FirstAsync();

        Assert.Equal(InstallAction.Uninstall, engine.LastPlannedAction);
        Assert.True(engine.ApplyCalled);
    }
}
