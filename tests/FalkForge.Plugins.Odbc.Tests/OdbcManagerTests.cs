namespace FalkForge.Plugins.Odbc.Tests;

using FalkForge.Plugins;
using Xunit;

public sealed class OdbcManagerTests
{
    [Fact]
    public void DsnExists_empty_name_returns_failure()
    {
        var manager = new OdbcManager();
        var result = manager.DsnExists("");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void DsnExists_nonexistent_dsn_returns_false()
    {
        var manager = new OdbcManager();
        var result = manager.DsnExists("NONEXISTENT_DSN_TEST_12345");
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void Plugin_registers_IOdbcManager()
    {
        var registry = new PluginServiceRegistry();
        var plugin = new OdbcPlugin();
        plugin.RegisterServices(registry);

        IPluginServices services = registry;
        Assert.NotNull(services.GetService<IOdbcManager>());
    }
}
