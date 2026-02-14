namespace FalkInstaller.Ui.Abstractions.Tests;

using FalkInstaller.Ui.Abstractions.ViewModels;
using Xunit;

public class InstallerShellViewModelTests
{
    private readonly TestInstallerEngine _engine = new();

    private TestShellViewModel CreateShell() => new(_engine);

    private TestPageViewModel CreatePage(TestShellViewModel shell, string title = "Page")
        => new(_engine, shell, title);

    [Fact]
    public void CurrentPage_WithNoPages_ReturnsNull()
    {
        var shell = CreateShell();

        Assert.Null(shell.CurrentPage);
    }

    [Fact]
    public void RegisterPage_SetsFirstPageAsCurrent()
    {
        var shell = CreateShell();
        var page = CreatePage(shell, "First");

        shell.RegisterPage(page);

        Assert.Same(page, shell.CurrentPage);
    }

    [Fact]
    public void RegisterPage_SecondPage_DoesNotChangeCurrent()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");

        shell.RegisterPage(first);
        shell.RegisterPage(second);

        Assert.Same(first, shell.CurrentPage);
    }

    [Fact]
    public void Pages_ReturnsAllRegisteredPages()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");

        shell.RegisterPage(first);
        shell.RegisterPage(second);

        Assert.Equal(2, shell.Pages.Count);
        Assert.Same(first, shell.Pages[0]);
        Assert.Same(second, shell.Pages[1]);
    }

    [Fact]
    public void CanGoNext_WithSinglePage_ReturnsFalse()
    {
        var shell = CreateShell();
        shell.RegisterPage(CreatePage(shell));

        Assert.False(shell.CanGoNext);
    }

    [Fact]
    public void CanGoNext_WithMultiplePages_ReturnsTrue()
    {
        var shell = CreateShell();
        shell.RegisterPage(CreatePage(shell, "First"));
        shell.RegisterPage(CreatePage(shell, "Second"));

        Assert.True(shell.CanGoNext);
    }

    [Fact]
    public void CanGoBack_AtFirstPage_ReturnsFalse()
    {
        var shell = CreateShell();
        shell.RegisterPage(CreatePage(shell, "First"));
        shell.RegisterPage(CreatePage(shell, "Second"));

        Assert.False(shell.CanGoBack);
    }

    [Fact]
    public void NavigateNext_MovesToNextPage()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");
        shell.RegisterPage(first);
        shell.RegisterPage(second);

        shell.NavigateNext();

        Assert.Same(second, shell.CurrentPage);
    }

    [Fact]
    public void NavigateNext_AtLastPage_DoesNothing()
    {
        var shell = CreateShell();
        var only = CreatePage(shell, "Only");
        shell.RegisterPage(only);

        shell.NavigateNext();

        Assert.Same(only, shell.CurrentPage);
    }

    [Fact]
    public void NavigateBack_MovesToPreviousPage()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");
        shell.RegisterPage(first);
        shell.RegisterPage(second);
        shell.NavigateNext();

        shell.NavigateBack();

        Assert.Same(first, shell.CurrentPage);
    }

    [Fact]
    public void NavigateBack_AtFirstPage_DoesNothing()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        shell.RegisterPage(first);

        shell.NavigateBack();

        Assert.Same(first, shell.CurrentPage);
    }

    [Fact]
    public void CanGoBack_AfterNavigateNext_ReturnsTrue()
    {
        var shell = CreateShell();
        shell.RegisterPage(CreatePage(shell, "First"));
        shell.RegisterPage(CreatePage(shell, "Second"));
        shell.NavigateNext();

        Assert.True(shell.CanGoBack);
    }

    [Fact]
    public void CanGoNext_AtLastPage_ReturnsFalse()
    {
        var shell = CreateShell();
        shell.RegisterPage(CreatePage(shell, "First"));
        shell.RegisterPage(CreatePage(shell, "Second"));
        shell.NavigateNext();

        Assert.False(shell.CanGoNext);
    }

    [Fact]
    public void NavigateNext_CallsOnNavigatingFromOnCurrentPage()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");
        shell.RegisterPage(first);
        shell.RegisterPage(second);

        shell.NavigateNext();

        Assert.Equal(1, first.NavigatingFromCount);
    }

    [Fact]
    public void NavigateNext_CallsOnNavigatedToOnTargetPage()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");
        shell.RegisterPage(first);
        shell.RegisterPage(second);

        shell.NavigateNext();

        Assert.Equal(1, second.NavigatedToCount);
    }

    [Fact]
    public void NavigateBack_CallsOnNavigatingFromOnCurrentPage()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");
        shell.RegisterPage(first);
        shell.RegisterPage(second);
        shell.NavigateNext();

        shell.NavigateBack();

        Assert.Equal(1, second.NavigatingFromCount);
    }

    [Fact]
    public void NavigateBack_CallsOnNavigatedToOnTargetPage()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");
        shell.RegisterPage(first);
        shell.RegisterPage(second);
        shell.NavigateNext();

        shell.NavigateBack();

        Assert.Equal(1, first.NavigatedToCount);
    }

    [Fact]
    public void NavigateNext_WhenCanNavigateNextIsFalse_DoesNotNavigate()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        first.AllowNavigateNext = false;
        var second = CreatePage(shell, "Second");
        shell.RegisterPage(first);
        shell.RegisterPage(second);

        shell.NavigateNext();

        Assert.Same(first, shell.CurrentPage);
    }

    [Fact]
    public void NavigateBack_WhenCanNavigateBackIsFalse_DoesNotNavigate()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");
        second.AllowNavigateBack = false;
        shell.RegisterPage(first);
        shell.RegisterPage(second);
        shell.NavigateNext();

        shell.NavigateBack();

        Assert.Same(second, shell.CurrentPage);
    }

    [Fact]
    public void NavigateTo_ByPageInstance_NavigatesToCorrectPage()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");
        var third = CreatePage(shell, "Third");
        shell.RegisterPage(first);
        shell.RegisterPage(second);
        shell.RegisterPage(third);

        shell.NavigateTo(third);

        Assert.Same(third, shell.CurrentPage);
    }

    [Fact]
    public void NavigateTo_WithUnregisteredPage_DoesNothing()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var unregistered = CreatePage(shell, "Unregistered");
        shell.RegisterPage(first);

        shell.NavigateTo(unregistered);

        Assert.Same(first, shell.CurrentPage);
    }

    [Fact]
    public void NavigateTo_ByType_NavigatesToCorrectPage()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var special = new AnotherTestPageViewModel(_engine, shell);
        shell.RegisterPage(first);
        shell.RegisterPage(special);

        shell.NavigateTo<AnotherTestPageViewModel>();

        Assert.Same(special, shell.CurrentPage);
    }

    [Fact]
    public void NavigateTo_ByType_WhenTypeNotRegistered_DoesNothing()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        shell.RegisterPage(first);

        shell.NavigateTo<AnotherTestPageViewModel>();

        Assert.Same(first, shell.CurrentPage);
    }

    [Fact]
    public void NavigateTo_CallsLifecycleMethods()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");
        var third = CreatePage(shell, "Third");
        shell.RegisterPage(first);
        shell.RegisterPage(second);
        shell.RegisterPage(third);

        shell.NavigateTo(third);

        Assert.Equal(1, first.NavigatingFromCount);
        Assert.Equal(1, third.NavigatedToCount);
    }

    [Fact]
    public void NavigateNext_FiresOnCurrentPageChanged()
    {
        var shell = CreateShell();
        shell.RegisterPage(CreatePage(shell, "First"));
        shell.RegisterPage(CreatePage(shell, "Second"));

        shell.NavigateNext();

        Assert.Equal(1, shell.PageChangedCount);
    }

    [Fact]
    public void NavigateBack_FiresOnCurrentPageChanged()
    {
        var shell = CreateShell();
        shell.RegisterPage(CreatePage(shell, "First"));
        shell.RegisterPage(CreatePage(shell, "Second"));
        shell.NavigateNext();

        shell.NavigateBack();

        Assert.Equal(2, shell.PageChangedCount);
    }

    [Fact]
    public void NavigateTo_FiresOnCurrentPageChanged()
    {
        var shell = CreateShell();
        var first = CreatePage(shell, "First");
        var second = CreatePage(shell, "Second");
        shell.RegisterPage(first);
        shell.RegisterPage(second);

        shell.NavigateTo(second);

        Assert.Equal(1, shell.PageChangedCount);
    }

    [Fact]
    public void Engine_ReturnsInjectedEngine()
    {
        var shell = CreateShell();

        Assert.Same(_engine, shell.Engine);
    }

    [Fact]
    public void CanGoNext_WithNoPages_ReturnsFalse()
    {
        var shell = CreateShell();

        Assert.False(shell.CanGoNext);
    }

    [Fact]
    public void CanGoBack_WithNoPages_ReturnsFalse()
    {
        var shell = CreateShell();

        Assert.False(shell.CanGoBack);
    }
}
