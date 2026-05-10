namespace FalkForge.Ui.Abstractions.Tests.ViewModels;

using FalkForge.Ui.Abstractions.ViewModels;
using Xunit;

/// <summary>
/// Verifies that navigation methods return Task so exceptions propagate observably
/// rather than escaping to the dispatcher as unhandled exceptions.
/// </summary>
public class InstallerShellViewModelNavigationTaskTests
{
    private readonly TestInstallerEngine _engine = new();

    private TestShellViewModel CreateShell() => new(_engine);

    private ThrowingPageViewModel CreateThrowingPage(TestShellViewModel shell, string title = "Throwing")
        => new(_engine, shell, title);

    // ── NavigateNext ────────────────────────────────────────────────────────

    [Fact]
    public async Task NavigateNext_WhenNavigatedToThrows_TaskFaults()
    {
        var shell = CreateShell();
        var first = new TestPageViewModel(_engine, shell, "First");
        var throwing = CreateThrowingPage(shell, "Throwing");

        shell.RegisterPage(first);
        shell.RegisterPage(throwing);

        // NavigateNext must return Task so the caller can observe the fault.
        var task = shell.NavigateNext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    [Fact]
    public async Task NavigateNext_WhenNavigatingFromThrows_TaskFaults()
    {
        var shell = CreateShell();
        var throwing = CreateThrowingPage(shell, "Throwing");
        throwing.ThrowOnNavigatingFrom = true;
        var second = new TestPageViewModel(_engine, shell, "Second");

        shell.RegisterPage(throwing);
        shell.RegisterPage(second);

        var task = shell.NavigateNext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    // ── NavigateBack ────────────────────────────────────────────────────────

    [Fact]
    public async Task NavigateBack_WhenNavigatedToThrows_TaskFaults()
    {
        var shell = CreateShell();
        var throwing = CreateThrowingPage(shell, "Throwing");
        var second = new TestPageViewModel(_engine, shell, "Second");

        shell.RegisterPage(throwing);
        shell.RegisterPage(second);

        // Move to second first (normally), then back.
        await shell.NavigateNext();

        var task = shell.NavigateBack();
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    [Fact]
    public async Task NavigateBack_WhenNavigatingFromThrows_TaskFaults()
    {
        var shell = CreateShell();
        var first = new TestPageViewModel(_engine, shell, "First");
        var throwing = CreateThrowingPage(shell, "Throwing");
        // Only throw on NavigatingFrom, not NavigatedTo — so NavigateNext can land here first.
        throwing.ThrowOnNavigatedTo = false;
        throwing.ThrowOnNavigatingFrom = true;

        shell.RegisterPage(first);
        shell.RegisterPage(throwing);
        await shell.NavigateNext();  // lands on throwing (NavigatedTo OK)

        var task = shell.NavigateBack();
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    // ── NavigateTo ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NavigateTo_WhenNavigatedToThrows_TaskFaults()
    {
        var shell = CreateShell();
        var first = new TestPageViewModel(_engine, shell, "First");
        var throwing = CreateThrowingPage(shell, "Throwing");

        shell.RegisterPage(first);
        shell.RegisterPage(throwing);

        var task = shell.NavigateTo(throwing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    [Fact]
    public async Task NavigateTo_WhenNavigatingFromThrows_TaskFaults()
    {
        var shell = CreateShell();
        var throwing = CreateThrowingPage(shell, "Throwing");
        throwing.ThrowOnNavigatingFrom = true;
        var second = new TestPageViewModel(_engine, shell, "Second");

        shell.RegisterPage(throwing);
        shell.RegisterPage(second);

        var task = shell.NavigateTo(second);
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    // ── Helper: page that throws on lifecycle hooks ──────────────────────────

    private sealed class ThrowingPageViewModel : InstallerPageViewModel
    {
        public bool ThrowOnNavigatedTo { get; set; } = true;
        public bool ThrowOnNavigatingFrom { get; set; }

        public ThrowingPageViewModel(IInstallerEngine engine, INavigationService nav, string title)
            : base(engine, nav)
        {
            Title = title;
        }

        public override string Title { get; }
        public override string Description => string.Empty;

        public override Task OnNavigatedToAsync(CancellationToken ct = default)
        {
            if (ThrowOnNavigatedTo)
                throw new InvalidOperationException("Simulated lifecycle failure on NavigatedTo");
            return Task.CompletedTask;
        }

        public override Task OnNavigatingFromAsync(CancellationToken ct = default)
        {
            if (ThrowOnNavigatingFrom)
                throw new InvalidOperationException("Simulated lifecycle failure on NavigatingFrom");
            return Task.CompletedTask;
        }
    }
}
