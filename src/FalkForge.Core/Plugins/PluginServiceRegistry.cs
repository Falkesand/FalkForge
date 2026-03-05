namespace FalkForge.Plugins;

public sealed class PluginServiceRegistry : IPluginServiceRegistry, IPluginServices
{
    private readonly Dictionary<Type, Func<object>> _factories = new();
    private bool _frozen;

    public void Register<TService>(TService instance) where TService : class
    {
        if (_frozen) throw new InvalidOperationException("Plugin registry is frozen after initialization.");
        _factories.TryAdd(typeof(TService), () => instance);
    }

    public void Register<TService>(Func<TService> factory) where TService : class
    {
        if (_frozen) throw new InvalidOperationException("Plugin registry is frozen after initialization.");
        _factories.TryAdd(typeof(TService), () => factory());
    }

    public TService? GetService<TService>() where TService : class
    {
        return _factories.TryGetValue(typeof(TService), out var factory) ? (TService)factory() : null;
    }

    public TService GetRequiredService<TService>() where TService : class
    {
        return GetService<TService>() ?? throw new InvalidOperationException(
            $"No plugin service registered for {typeof(TService).Name}. Ensure the required plugin is registered via Plugin<T>().");
    }

    public void Freeze()
    {
        _frozen = true;
    }
}