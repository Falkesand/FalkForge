namespace FalkForge.Plugins.Odbc;

/// <summary>
/// Registers the ODBC data source manager service.
/// </summary>
public sealed class OdbcPlugin : IInstallerPlugin
{
    public string Name => "ODBC";

    public void RegisterServices(IPluginServiceRegistry registry)
    {
        registry.Register<IOdbcManager>(new OdbcManager());
    }
}