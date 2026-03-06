namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.ViewModels;
using Xunit;

public class CustomShellViewModelUpdateTests
{
    private static TestInstallerEngine CreateEngineWithFeed(UpdatePolicy policy, bool promptBeforeAutoUpdate = false)
    {
        var feed = new ManifestUpdateFeed(
            "https://example.com/feed",
            policy,
            AllowResumeDownload: false,
            PromptBeforeAutoUpdate: promptBeforeAutoUpdate);

        var engine = new TestInstallerEngine
        {
            Manifest = new InstallerManifest
            {
                Name = "TestProduct",
                Manufacturer = "TestCorp",
                Version = "1.0.0",
                BundleId = Guid.NewGuid(),
                UpgradeCode = Guid.NewGuid(),
                Packages = [],
                Scope = InstallScope.PerUser,
                UpdateFeed = feed
            }
        };
        return engine;
    }

    private static CustomShellViewModel CreateVmWithEngine(
        IReadOnlyList<InstallerPage> pages,
        IInstallerEngine engine)
    {
        var state = new InstallerState();
        foreach (var page in pages)
        {
            page.Engine = engine;
            page.SharedState = state;
        }

        return new CustomShellViewModel(pages, engine, state);
    }

    [WpfFact]
    public async Task UpdateReady_DownloadAndPrompt_InsertsUpdatePage()
    {
        var engine = CreateEngineWithFeed(UpdatePolicy.DownloadAndPrompt);
        var pages = new InstallerPage[] { new PageOne(), new PageTwo() };
        var vm = CreateVmWithEngine(pages, engine);
        await vm.NavigateToFirstPageAsync();

        await vm.HandleUpdateReadyAsync("2.0.0", @"C:\cache\update.exe");

        Assert.IsType<UpdateAvailableInstallerPage>(vm.CurrentPage);
    }

    [WpfFact]
    public async Task UpdateReady_NotifyOnly_DoesNotInsertUpdatePage()
    {
        var engine = CreateEngineWithFeed(UpdatePolicy.NotifyOnly);
        var pages = new InstallerPage[] { new PageOne(), new PageTwo() };
        var vm = CreateVmWithEngine(pages, engine);
        await vm.NavigateToFirstPageAsync();

        await vm.HandleUpdateReadyAsync("2.0.0", @"C:\cache\update.exe");

        // Should remain on PageOne, no insertion
        Assert.IsType<PageOne>(vm.CurrentPage);
    }

    [WpfFact]
    public async Task UpdateReady_AutoUpdate_WithPrompt_InsertsUpdatePage()
    {
        var engine = CreateEngineWithFeed(UpdatePolicy.AutoUpdate, promptBeforeAutoUpdate: true);
        var pages = new InstallerPage[] { new PageOne(), new PageTwo() };
        var vm = CreateVmWithEngine(pages, engine);
        await vm.NavigateToFirstPageAsync();

        await vm.HandleUpdateReadyAsync("2.0.0", @"C:\cache\update.exe");

        Assert.IsType<UpdateAvailableInstallerPage>(vm.CurrentPage);
    }

    [WpfFact]
    public async Task UpdateReady_AutoUpdate_WithoutPrompt_DoesNotInsertUpdatePage()
    {
        var engine = CreateEngineWithFeed(UpdatePolicy.AutoUpdate, promptBeforeAutoUpdate: false);
        var pages = new InstallerPage[] { new PageOne(), new PageTwo() };
        var vm = CreateVmWithEngine(pages, engine);
        await vm.NavigateToFirstPageAsync();

        await vm.HandleUpdateReadyAsync("2.0.0", @"C:\cache\update.exe");

        Assert.IsType<PageOne>(vm.CurrentPage);
    }

    [WpfFact]
    public async Task UpdateReady_NoUpdateFeed_DoesNotInsertUpdatePage()
    {
        var engine = new TestInstallerEngine(); // No UpdateFeed
        var pages = new InstallerPage[] { new PageOne(), new PageTwo() };
        var vm = CreateVmWithEngine(pages, engine);
        await vm.NavigateToFirstPageAsync();

        await vm.HandleUpdateReadyAsync("2.0.0", @"C:\cache\update.exe");

        Assert.IsType<PageOne>(vm.CurrentPage);
    }

    [WpfFact]
    public async Task UpdateReady_InsertedPage_HasCorrectVersion()
    {
        var engine = CreateEngineWithFeed(UpdatePolicy.DownloadAndPrompt);
        var pages = new InstallerPage[] { new PageOne(), new PageTwo() };
        var vm = CreateVmWithEngine(pages, engine);
        await vm.NavigateToFirstPageAsync();

        await vm.HandleUpdateReadyAsync("3.5.0", @"C:\cache\update.exe");

        var updatePage = Assert.IsType<UpdateAvailableInstallerPage>(vm.CurrentPage);
        Assert.Equal("3.5.0", updatePage.UpdateVersion);
        Assert.Equal(@"C:\cache\update.exe", updatePage.CachedFilePath);
    }
}
