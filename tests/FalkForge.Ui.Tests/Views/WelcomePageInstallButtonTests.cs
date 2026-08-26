namespace FalkForge.Ui.Tests.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FalkForge.Ui.Tests.ViewModels;
using FalkForge.Ui.ViewModels;
using FalkForge.Ui.Views;
using Xunit;

/// <summary>
/// The Install button on the welcome page bound Content, Style, Width and Visibility, and nothing
/// else. Pressing it did nothing at all: no command, no click handler. It is the button the page
/// puts in front of a user on a machine where the product is not installed.
/// </summary>
public class WelcomePageInstallButtonTests
{
    [WpfFact]
    public void TheInstallButtonIsBoundToACommand()
    {
        var shell = new DefaultShellViewModel(new TestInstallerEngine());
        var page = new WelcomePage
        {
            DataContext = shell.Pages.OfType<WelcomePageViewModel>().Single()
        };

        var host = new Border { Child = page };
        var slot = new Size(484, 328);
        host.Measure(slot);
        host.Arrange(new Rect(new Point(0, 0), slot));
        host.UpdateLayout();

        var install = FindButton(page, "Install");
        Assert.NotNull(install);
        Assert.NotNull(install.Command);
    }

    [Fact]
    public async Task StartingTheInstallMovesOffTheWelcomePage()
    {
        var shell = new DefaultShellViewModel(new TestInstallerEngine());
        var welcome = shell.Pages.OfType<WelcomePageViewModel>().Single();

        await welcome.StartInstallAsync();

        Assert.IsType<LicensePageViewModel>(shell.CurrentPage);
    }

    private static Button? FindButton(DependencyObject root, string content)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && Equals(button.Content, content))
                return button;

            if (FindButton(child, content) is { } deeper)
                return deeper;
        }

        return null;
    }
}
