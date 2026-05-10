using FalkForge.Plugins;
using FalkForge.Ui.Localization;

namespace FalkForge.Ui;

public sealed class InstallerUIBuilder
{
    private readonly PageRegistrar _pageRegistrar = new();
    private readonly List<IInstallerPlugin> _plugins = [];
    private PluginRegistry? _pluginRegistry;

    internal InstallerWindowConfig WindowConfig { get; private set; } = new();

    internal IReadOnlyList<Func<InstallerPage>> PageFactories => _pageRegistrar.Factories;
    internal IReadOnlyList<IInstallerPlugin> Plugins => _plugins;

    /// <summary>
    /// A <see cref="PluginRegistry"/> supplied via <see cref="RegisterPlugins(PluginRegistry)"/>,
    /// or <c>null</c> when no bulk registry was provided. Applied after per-type <see cref="Plugin{T}()"/>
    /// registrations so that explicit entries take priority.
    /// </summary>
    internal PluginRegistry? BulkPluginRegistry => _pluginRegistry;

    internal UiLocalizationConfig? LocalizationConfig { get; private set; }

    public InstallerUIBuilder Window(Action<InstallerWindowBuilder> configure)
    {
        var builder = new InstallerWindowBuilder();
        configure(builder);
        WindowConfig = builder.Build();
        return this;
    }

    public InstallerUIBuilder Pages(Action<PageRegistrar> configure)
    {
        configure(_pageRegistrar);
        return this;
    }

    public InstallerUIBuilder Plugin<T>() where T : IInstallerPlugin, new()
    {
        _plugins.Add(new T());
        return this;
    }

    /// <summary>
    /// Adds all plugins from a pre-built <see cref="PluginRegistry"/> to this builder.
    /// Combines with any prior <see cref="Plugin{T}()"/> calls; earlier per-type registrations
    /// are applied first, so first registration wins when both paths register the same service type.
    /// </summary>
    public InstallerUIBuilder RegisterPlugins(PluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _pluginRegistry = registry;
        return this;
    }

    public InstallerUIBuilder Localization(Action<UiLocalizationBuilder> configure)
    {
        var builder = new UiLocalizationBuilder();
        configure(builder);
        LocalizationConfig = builder.Build();
        return this;
    }
}