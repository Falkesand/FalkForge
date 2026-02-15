namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Ui.ViewModels;
using Xunit;

public class DefaultShellViewModelTests
{
    private readonly TestInstallerEngine _engine = new();

    [Fact]
    public void Constructor_RegistersSevenPages()
    {
        var shell = new DefaultShellViewModel(_engine);

        Assert.Equal(7, shell.Pages.Count);
    }

    [Fact]
    public void Constructor_FirstPageIsWelcome()
    {
        var shell = new DefaultShellViewModel(_engine);

        Assert.IsType<WelcomePageViewModel>(shell.CurrentPage);
    }

    [Fact]
    public void Pages_ContainsAllExpectedTypes()
    {
        var shell = new DefaultShellViewModel(_engine);

        Assert.IsType<WelcomePageViewModel>(shell.Pages[0]);
        Assert.IsType<LicensePageViewModel>(shell.Pages[1]);
        Assert.IsType<InstallDirPageViewModel>(shell.Pages[2]);
        Assert.IsType<FeaturesPageViewModel>(shell.Pages[3]);
        Assert.IsType<MaintenancePageViewModel>(shell.Pages[4]);
        Assert.IsType<ProgressPageViewModel>(shell.Pages[5]);
        Assert.IsType<CompletePageViewModel>(shell.Pages[6]);
    }

    [Fact]
    public void CanGoNext_OnFirstPage_ReturnsTrue()
    {
        var shell = new DefaultShellViewModel(_engine);

        // Welcome page CanNavigateNext is true by default
        Assert.True(shell.CanGoNext);
    }

    [Fact]
    public void CanGoBack_OnFirstPage_ReturnsFalse()
    {
        var shell = new DefaultShellViewModel(_engine);

        Assert.False(shell.CanGoBack);
    }

    [Fact]
    public void NavigateNext_RaisesPropertyChanged_ForCurrentPage()
    {
        var shell = new DefaultShellViewModel(_engine);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        shell.NavigateNext();

        Assert.Contains(nameof(shell.CurrentPage), changed);
    }

    [Fact]
    public void NavigateNext_RaisesPropertyChanged_ForCanGoBack()
    {
        var shell = new DefaultShellViewModel(_engine);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        shell.NavigateNext();

        Assert.Contains(nameof(shell.CanGoBack), changed);
    }

    [Fact]
    public void NavigateNext_RaisesPropertyChanged_ForCanGoNext()
    {
        var shell = new DefaultShellViewModel(_engine);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        shell.NavigateNext();

        Assert.Contains(nameof(shell.CanGoNext), changed);
    }

    [Fact]
    public void Engine_ReturnsInjectedEngine()
    {
        var shell = new DefaultShellViewModel(_engine);

        Assert.Same(_engine, shell.Engine);
    }

    [Fact]
    public void NavigateNext_MovesToLicensePage()
    {
        var shell = new DefaultShellViewModel(_engine);

        shell.NavigateNext();

        Assert.IsType<LicensePageViewModel>(shell.CurrentPage);
    }
}
