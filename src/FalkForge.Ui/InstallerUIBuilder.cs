namespace FalkForge.Ui;

using FalkForge.Plugins;
using FalkForge.Ui.Localization;

public sealed class InstallerUIBuilder
{
    private InstallerWindowConfig _windowConfig = new();
    private readonly PageRegistrar _pageRegistrar = new();
    private readonly List<IInstallerPlugin> _plugins = [];
    private UiLocalizationConfig? _localizationConfig;

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

    public InstallerUIBuilder Localization(Action<UiLocalizationBuilder> configure)
    {
        var builder = new UiLocalizationBuilder();
        configure(builder);
        _localizationConfig = builder.Build();
        return this;
    }

    internal InstallerWindowConfig WindowConfig => _windowConfig;
    internal IReadOnlyList<Func<InstallerPage>> PageFactories => _pageRegistrar.Factories;
    internal IReadOnlyList<IInstallerPlugin> Plugins => _plugins;
    internal UiLocalizationConfig? LocalizationConfig => _localizationConfig;
}
