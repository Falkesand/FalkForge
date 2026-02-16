namespace FalkForge.Ui;

public sealed class InstallerUIBuilder
{
    private InstallerWindowConfig _windowConfig = new();
    private readonly PageRegistrar _pageRegistrar = new();

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

    internal InstallerWindowConfig WindowConfig => _windowConfig;
    internal IReadOnlyList<Func<InstallerPage>> PageFactories => _pageRegistrar.Factories;
}
