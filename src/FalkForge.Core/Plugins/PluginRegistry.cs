namespace FalkForge.Plugins;

/// <summary>
/// An immutable, ordered collection of <see cref="IInstallerPlugin"/> instances that
/// can bulk-register their services into an <see cref="IPluginServiceRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PluginRegistry"/> is the AOT-safe composition root for plugins.
/// All plugin instances are supplied at compile time — no <c>Assembly.GetTypes()</c>,
/// <c>Activator.CreateInstance(string)</c>, or <c>Type.GetType(string)</c> calls.
/// </para>
/// <para>
/// First-registration-wins semantics are preserved: if two plugins attempt to register
/// the same service type, the one listed first in the <see cref="Create"/> call wins.
/// </para>
/// <example>
/// <code>
/// // Composition root (AOT-safe):
/// var registry = PluginRegistry.Create(new SqlPlugin(), new OdbcPlugin(), new FileSystemPlugin());
/// registry.RegisterAll(serviceRegistry);
/// </code>
/// </example>
/// </remarks>
public sealed class PluginRegistry
{
    private readonly IInstallerPlugin[] _plugins;

    private PluginRegistry(IInstallerPlugin[] plugins) => _plugins = plugins;

    /// <summary>Gets the number of plugins in this registry.</summary>
    public int Count => _plugins.Length;

    /// <summary>Gets the names of all registered plugins, in registration order.</summary>
    public IReadOnlyList<string> PluginNames
    {
        get
        {
            var names = new string[_plugins.Length];
            for (var i = 0; i < _plugins.Length; i++)
                names[i] = _plugins[i].Name;
            return names;
        }
    }

    /// <summary>
    /// Creates a <see cref="PluginRegistry"/> containing the supplied plugins.
    /// </summary>
    /// <param name="plugins">
    /// Plugin instances in priority order. When two plugins register the same service,
    /// the one that appears earlier in this list wins.
    /// </param>
    public static PluginRegistry Create(params IInstallerPlugin[] plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        return new PluginRegistry(plugins);
    }

    /// <summary>
    /// Calls <see cref="IInstallerPlugin.RegisterServices"/> on every plugin in order,
    /// populating <paramref name="registry"/> with their services.
    /// </summary>
    /// <param name="registry">
    /// The target service registry. Must not be frozen; throws
    /// <see cref="InvalidOperationException"/> if it is.
    /// </param>
    public void RegisterAll(IPluginServiceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var plugin in _plugins)
            plugin.RegisterServices(registry);
    }
}
