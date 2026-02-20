using Xunit;

namespace FalkForge.Core.Tests.Plugins;

using FalkForge.Plugins;

public sealed class PluginServiceRegistryTests
{
    [Fact]
    public void Register_instance_and_resolve()
    {
        var registry = new PluginServiceRegistry();
        var service = new FakeService();
        registry.Register<IFakeService>(service);

        IPluginServices services = registry;
        Assert.Same(service, services.GetService<IFakeService>());
    }

    [Fact]
    public void Register_factory_and_resolve()
    {
        var registry = new PluginServiceRegistry();
        registry.Register<IFakeService>(() => new FakeService());

        IPluginServices services = registry;
        Assert.NotNull(services.GetService<IFakeService>());
    }

    [Fact]
    public void GetService_unregistered_returns_null()
    {
        var registry = new PluginServiceRegistry();
        IPluginServices services = registry;
        Assert.Null(services.GetService<IFakeService>());
    }

    [Fact]
    public void GetRequiredService_unregistered_throws()
    {
        var registry = new PluginServiceRegistry();
        IPluginServices services = registry;
        Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IFakeService>());
    }

    [Fact]
    public void First_registration_wins()
    {
        var registry = new PluginServiceRegistry();
        var first = new FakeService();
        var second = new FakeService();
        registry.Register<IFakeService>(first);
        registry.Register<IFakeService>(second);

        IPluginServices services = registry;
        Assert.Same(first, services.GetService<IFakeService>());
    }

    [Fact]
    public void Freeze_prevents_registration()
    {
        var registry = new PluginServiceRegistry();
        registry.Freeze();
        Assert.Throws<InvalidOperationException>(() => registry.Register<IFakeService>(new FakeService()));
    }

    [Fact]
    public void Freeze_allows_resolve()
    {
        var registry = new PluginServiceRegistry();
        registry.Register<IFakeService>(new FakeService());
        registry.Freeze();

        IPluginServices services = registry;
        Assert.NotNull(services.GetService<IFakeService>());
    }

    private interface IFakeService { }
    private sealed class FakeService : IFakeService { }
}
