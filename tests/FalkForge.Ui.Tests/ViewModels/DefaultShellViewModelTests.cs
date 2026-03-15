namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
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

    [Fact]
    public void ForwardUpdateDownloadProgress_UpdatesWelcomePageViewModel()
    {
        var shell = new DefaultShellViewModel(_engine);

        // Current page is WelcomePageViewModel (index 0)
        shell.ForwardUpdateDownloadProgress(42, 420, 1000);

        var welcome = (WelcomePageViewModel)shell.CurrentPage!;
        Assert.Equal(42, welcome.DownloadPercent);
        Assert.True(welcome.IsDownloadingUpdate);
    }

    [Fact]
    public void ForwardUpdateDownloadProgress_WhenNotOnWelcomePage_DoesNotThrow()
    {
        var shell = new DefaultShellViewModel(_engine);
        shell.NavigateNext(); // Move to LicensePage

        // Should not throw when current page is not WelcomePageViewModel
        shell.ForwardUpdateDownloadProgress(50, 500, 1000);
    }

    [Fact]
    public async Task HandleUpdateReadyAsync_WithDownloadAndPromptPolicy_InsertsUpdatePage()
    {
        SetUpdateFeed(UpdatePolicy.DownloadAndPrompt);
        var shell = new DefaultShellViewModel(_engine);

        await shell.HandleUpdateReadyAsync("2.0.0", @"C:\cache\update.exe");

        // Update page should be inserted after current page (Welcome at 0), so at index 1
        Assert.IsType<UpdateAvailablePageViewModel>(shell.Pages[1]);
        Assert.Equal(8, shell.Pages.Count);
    }

    [Fact]
    public async Task HandleUpdateReadyAsync_WithDownloadAndPromptPolicy_NavigatesToUpdatePage()
    {
        SetUpdateFeed(UpdatePolicy.DownloadAndPrompt);
        var shell = new DefaultShellViewModel(_engine);

        await shell.HandleUpdateReadyAsync("2.0.0", @"C:\cache\update.exe");

        Assert.IsType<UpdateAvailablePageViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task HandleUpdateReadyAsync_WithAutoUpdateAndPrompt_InsertsUpdatePage()
    {
        SetUpdateFeed(UpdatePolicy.AutoUpdate, promptBeforeAutoUpdate: true);
        var shell = new DefaultShellViewModel(_engine);

        await shell.HandleUpdateReadyAsync("2.0.0", null);

        Assert.IsType<UpdateAvailablePageViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task HandleUpdateReadyAsync_WithAutoUpdateNoPrompt_DoesNotInsertPage()
    {
        SetUpdateFeed(UpdatePolicy.AutoUpdate, promptBeforeAutoUpdate: false);
        var shell = new DefaultShellViewModel(_engine);

        await shell.HandleUpdateReadyAsync("2.0.0", null);

        // No page inserted, still 7 pages
        Assert.Equal(7, shell.Pages.Count);
        Assert.IsType<WelcomePageViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task HandleUpdateReadyAsync_SetsVersionOnUpdatePage()
    {
        SetUpdateFeed(UpdatePolicy.DownloadAndPrompt);
        var shell = new DefaultShellViewModel(_engine);

        await shell.HandleUpdateReadyAsync("2.0.0", @"C:\cache\update.exe");

        var updatePage = (UpdateAvailablePageViewModel)shell.CurrentPage!;
        Assert.Equal("2.0.0", updatePage.UpdateVersion);
        Assert.Equal(@"C:\cache\update.exe", updatePage.CachedFilePath);
    }

    private void SetUpdateFeed(UpdatePolicy policy, bool promptBeforeAutoUpdate = false)
    {
        _engine.Manifest = new InstallerManifest
        {
            Name = "TestProduct",
            Manufacturer = "TestCorp",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Packages = [],
            Scope = InstallScope.PerUser,
            UpdateFeed = new ManifestUpdateFeed(
                "https://example.com/feed",
                policy,
                AllowResumeDownload: false,
                PromptBeforeAutoUpdate: promptBeforeAutoUpdate)
        };
    }
}
