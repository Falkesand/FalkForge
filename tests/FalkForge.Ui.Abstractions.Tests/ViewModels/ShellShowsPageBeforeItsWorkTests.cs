namespace FalkForge.Ui.Abstractions.Tests.ViewModels;

using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using Xunit;

/// <summary>
/// The shell told the window which page to show only after the incoming page's
/// <c>OnNavigatedToAsync</c> had finished. That was invisible while every page returned a completed
/// task, and wrong the moment one did real work: the wizard stayed on the page the user had just
/// left for the whole duration.
/// <para>
/// Measured on the real wizard once the progress page started running the install: pressing
/// <c>Next &gt;</c> on the Features page left Features on screen, and the progress page — the one
/// whose entire job is to show what is happening — never appeared while it happened.
/// </para>
/// </summary>
public class ShellShowsPageBeforeItsWorkTests
{
    [Fact]
    public async Task TheIncomingPageIsShownBeforeItStartsItsWork()
    {
        var engine = new TestInstallerEngine();
        var shell = new TestShellViewModel(engine);
        var first = new TestPageViewModel(engine, shell);
        var slow = new SlowPageViewModel(engine, shell);
        shell.RegisterPage(first);
        shell.RegisterPage(slow);

        var navigation = shell.NavigateNext();

        await slow.Entered.Task;
        Assert.Same(slow, shell.CurrentPage);
        Assert.Equal(1, shell.PageChangedCount);

        slow.Release.SetResult();
        await navigation;
    }

    private sealed class SlowPageViewModel : InstallerPageViewModel
    {
        public SlowPageViewModel(IInstallerEngine engine, INavigationService navigation)
            : base(engine, navigation)
        {
        }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string Title => "Slow";
        public override string Description => "Does real work on arrival";

        public override async Task OnNavigatedToAsync(CancellationToken ct = default)
        {
            Entered.SetResult();
            await Release.Task;
        }
    }
}
