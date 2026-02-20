namespace FalkForge.Ui;

using FalkForge.Plugins;

public sealed class InstallerUIBuilder
{
    private InstallerWindowConfig _windowConfig = new();
    private readonly PageRegistrar _pageRegistrar = new();
    private readonly List<IInstallerPlugin> _plugins = [];

    public InstallerUIBuilder Window(Action<InstallerWindowBuilder> configure)
    {
        var builder = new InstallerWindowBuilder();
        configure(builder);
        _windowConfig = builder.Build();
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

    internal InstallerWindowConfig WindowConfig => _windowConfig;
    internal IReadOnlyList<Func<InstallerPage>> PageFactories => _pageRegistrar.Factories;
    internal IReadOnlyList<IInstallerPlugin> Plugins => _plugins;
}
