namespace FalkForge.Plugins.Sql;

using FalkForge.Plugins;

public sealed class SqlPlugin : IInstallerPlugin
{
    public string Name => "SQL Server";

    public void RegisterServices(IPluginServiceRegistry registry)
    {
        registry.Register<ISqlServerDiscovery>(new SqlServerDiscovery());
        registry.Register<IDatabaseLister>(new DatabaseLister());
        registry.Register<IConnectionTester>(new ConnectionTester());
    }
}
