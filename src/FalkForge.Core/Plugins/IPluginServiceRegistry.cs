namespace FalkForge.Plugins;

public interface IPluginServiceRegistry
{
    void Register<TService>(TService instance) where TService : class;
    void Register<TService>(Func<TService> factory) where TService : class;
}