namespace FalkForge.Plugins;

public interface IInstallerPlugin
{
    string Name { get; }
    void RegisterServices(IPluginServiceRegistry registry);
}