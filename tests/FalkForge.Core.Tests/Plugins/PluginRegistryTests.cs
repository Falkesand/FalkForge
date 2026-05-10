using FalkForge.Plugins;
using Xunit;

namespace FalkForge.Core.Tests.Plugins;

/// <summary>
/// Tests for PluginRegistry — the static AOT-safe helper that collects IInstallerPlugin
/// instances and bulk-registers their services into an IPluginServiceRegistry.
/// </summary>
public sealed class PluginRegistryTests
{
    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_with_no_plugins_produces_empty_registry()
    {
        var registry = PluginRegistry.Create();
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Create_with_multiple_plugins_stores_all()
    {
        var registry = PluginRegistry.Create(new AlphaPlugin(), new BetaPlugin());
        Assert.Equal(2, registry.Count);
    }

    // ── RegisterAll ───────────────────────────────────────────────────────────

    [Fact]
    public void RegisterAll_calls_each_plugin_RegisterServices()
    {
        var registry = PluginRegistry.Create(new AlphaPlugin(), new BetaPlugin());
        var serviceRegistry = new PluginServiceRegistry();

        registry.RegisterAll(serviceRegistry);
        serviceRegistry.Freeze();

        IPluginServices services = serviceRegistry;
        Assert.NotNull(services.GetService<IAlphaService>());
        Assert.NotNull(services.GetService<IBetaService>());
    }

    [Fact]
    public void RegisterAll_preserves_first_registration_wins_across_plugins()
    {
        // Both AlphaPlugin and DuplicateAlphaPlugin register IAlphaService.
        // The first one (AlphaPlugin) should win.
        var registry = PluginRegistry.Create(new AlphaPlugin(), new DuplicateAlphaPlugin());
        var serviceRegistry = new PluginServiceRegistry();

        registry.RegisterAll(serviceRegistry);
        serviceRegistry.Freeze();

        IPluginServices services = serviceRegistry;
        var resolved = services.GetRequiredService<IAlphaService>();
        Assert.IsType<AlphaService>(resolved);
    }

    [Fact]
    public void RegisterAll_on_frozen_registry_throws()
    {
        var registry = PluginRegistry.Create(new AlphaPlugin());
        var serviceRegistry = new PluginServiceRegistry();
        serviceRegistry.Freeze();

        Assert.Throws<InvalidOperationException>(() => registry.RegisterAll(serviceRegistry));
    }

    [Fact]
    public void RegisterAll_empty_registry_is_no_op()
    {
        var registry = PluginRegistry.Create();
        var serviceRegistry = new PluginServiceRegistry();

        // Must not throw
        registry.RegisterAll(serviceRegistry);
        serviceRegistry.Freeze();
    }

    // ── Names ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Plugin_names_are_accessible_from_registry()
    {
        var registry = PluginRegistry.Create(new AlphaPlugin(), new BetaPlugin());
        var names = registry.PluginNames;
        Assert.Contains("Alpha", names);
        Assert.Contains("Beta", names);
    }

    // ── Fake plugins ─────────────────────────────────────────────────────────

    private interface IAlphaService { }
    private interface IBetaService { }

    private sealed class AlphaService : IAlphaService { }
    private sealed class BetaService : IBetaService { }
    private sealed class AltAlphaService : IAlphaService { }

    private sealed class AlphaPlugin : IInstallerPlugin
    {
        public string Name => "Alpha";
        public void RegisterServices(IPluginServiceRegistry registry) =>
            registry.Register<IAlphaService>(new AlphaService());
    }

    private sealed class BetaPlugin : IInstallerPlugin
    {
        public string Name => "Beta";
        public void RegisterServices(IPluginServiceRegistry registry) =>
            registry.Register<IBetaService>(new BetaService());
    }

    private sealed class DuplicateAlphaPlugin : IInstallerPlugin
    {
        public string Name => "DuplicateAlpha";
        public void RegisterServices(IPluginServiceRegistry registry) =>
            registry.Register<IAlphaService>(new AltAlphaService());
    }

}
