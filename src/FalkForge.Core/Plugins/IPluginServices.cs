namespace FalkForge.Plugins;

public interface IPluginServices
{
    TService? GetService<TService>() where TService : class;
    TService GetRequiredService<TService>() where TService : class;
}