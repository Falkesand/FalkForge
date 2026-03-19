namespace FalkForge.Plugins.Odbc;

public sealed class OdbcPlugin : IInstallerPlugin
{
    public string Name => "ODBC";

    public void RegisterServices(IPluginServiceRegistry registry)
    {
        registry.Register<IOdbcManager>(new OdbcManager());
    }
}