namespace FalkForge.Ui.Tests;

using System.Windows.Controls;
using FalkForge.Plugins;
using FalkForge.Ui.Abstractions;
using Xunit;

public class AppTestView : UserControl { }

public class TestPageForApp : InstallerPage<AppTestView>
{
    public override string Title => "Test";
}

public sealed class InstallerAppTests
{
    [Fact]
    public void Builder_ComposesWindowAndPages()
    {
        var builder = new InstallerUIBuilder();

        builder.Window(w => w.Size(800, 600).Title("Test"))
               .Pages(p => p.Add<TestPageForApp>());

        Assert.Equal(800, builder.WindowConfig.Width);
        Assert.Equal(600, builder.WindowConfig.Height);
        Assert.Equal("Test", builder.WindowConfig.Title);
        Assert.Single(builder.PageFactories);
    }

    [Fact]
    public void PageFactories_CreateDistinctInstances()
    {
        var builder = new InstallerUIBuilder();
        builder.Pages(p => p.Add<TestPageForApp>());

        var page1 = builder.PageFactories[0]();
        var page2 = builder.PageFactories[0]();

        Assert.NotSame(page1, page2);
    }

    [Fact]
    public void Pages_CanBeWiredWithSharedState()
    {
        var builder = new InstallerUIBuilder();
        builder.Pages(p => p.Add<TestPageForApp>());

        var page = builder.PageFactories[0]();
        var state = new InstallerState();
        page.SharedState = state;

        Assert.Same(state, page.SharedState);
    }

    [Fact]
    public void Builder_DefaultWindowConfig_HasExpectedDefaults()
    {
        var builder = new InstallerUIBuilder();

        Assert.Equal(600, builder.WindowConfig.Width);
        Assert.Equal(400, builder.WindowConfig.Height);
        Assert.False(builder.WindowConfig.IsBorderless);
    }

    [Fact]
    public void RegisterPlugins_and_Plugin_both_honoured_first_registration_wins()
    {
        // Arrange: Plugin<T>() registers AlphaPlugin first; RegisterPlugins adds a
        // PluginRegistry that also contains AlphaPlugin plus a BetaPlugin.
        // Expected: AlphaService from the per-type path wins (first-wins); BetaService
        // from the bulk path is also registered (not silently dropped).
        var builder = new InstallerUIBuilder();
        builder.Plugin<AlphaPlugin>();
        builder.RegisterPlugins(PluginRegistry.Create(new DuplicateAlphaPlugin(), new BetaPlugin()));

        // Act — mirror what InstallerApp.RunCore does
        var serviceRegistry = new PluginServiceRegistry();
        foreach (var plugin in builder.Plugins)
            plugin.RegisterServices(serviceRegistry);
        builder.BulkPluginRegistry?.RegisterAll(serviceRegistry);
        serviceRegistry.Freeze();

        IPluginServices services = serviceRegistry;

        // AlphaPlugin (per-type, first) wins over DuplicateAlphaPlugin (bulk, second)
        Assert.IsType<AlphaService>(services.GetRequiredService<IAlphaService>());
        // BetaPlugin from bulk path is still registered — not silently dropped
        Assert.NotNull(services.GetService<IBetaService>());
    }

    // ── Fake plugins for combined-path test ───────────────────────────────────

    private interface IAlphaService { }
    private interface IBetaService { }

    private sealed class AlphaService : IAlphaService { }
    private sealed class AltAlphaService : IAlphaService { }
    private sealed class BetaService : IBetaService { }

    private sealed class AlphaPlugin : IInstallerPlugin
    {
        public string Name => "Alpha";
        public void RegisterServices(IPluginServiceRegistry registry) =>
            registry.Register<IAlphaService>(new AlphaService());
    }

    private sealed class DuplicateAlphaPlugin : IInstallerPlugin
    {
        public string Name => "DuplicateAlpha";
        public void RegisterServices(IPluginServiceRegistry registry) =>
            registry.Register<IAlphaService>(new AltAlphaService());
    }

    private sealed class BetaPlugin : IInstallerPlugin
    {
        public string Name => "Beta";
        public void RegisterServices(IPluginServiceRegistry registry) =>
            registry.Register<IBetaService>(new BetaService());
    }
}
