using FalkForge.Plugins;
using FalkForge.Ui.Localization;

namespace FalkForge.Ui;

public sealed class InstallerUIBuilder
{
    private readonly PageRegistrar _pageRegistrar = new();
    private readonly List<IInstallerPlugin> _plugins = [];

    internal InstallerWindowConfig WindowConfig { get; private set; } = new();

    internal IReadOnlyList<Func<InstallerPage>> PageFactories => _pageRegistrar.Factories;
    internal IReadOnlyList<IInstallerPlugin> Plugins => _plugins;
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

    public InstallerUIBuilder Localization(Action<UiLocalizationBuilder> configure)
    {
        var builder = new UiLocalizationBuilder();
        configure(builder);
        LocalizationConfig = builder.Build();
        return this;
    }
}