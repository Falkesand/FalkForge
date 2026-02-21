using FalkForge.Plugins;
using Xunit;

namespace FalkForge.Plugins.Sql.Tests;

public sealed class SqlPluginTests
{
    [Fact]
    public void Plugin_registers_all_services()
    {
        var registry = new PluginServiceRegistry();
        var plugin = new SqlPlugin();
        plugin.RegisterServices(registry);

        IPluginServices services = registry;
        Assert.NotNull(services.GetService<ISqlServerDiscovery>());
        Assert.NotNull(services.GetService<IDatabaseLister>());
        Assert.NotNull(services.GetService<IConnectionTester>());
    }
}
