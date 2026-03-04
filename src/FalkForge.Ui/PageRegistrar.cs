namespace FalkForge.Ui;

public sealed class PageRegistrar
{
    private readonly List<Func<InstallerPage>> _factories = new();

    internal IReadOnlyList<Func<InstallerPage>> Factories => _factories.AsReadOnly();

    public PageRegistrar Add<TPage>() where TPage : InstallerPage, new()
    {
        _factories.Add(() => new TPage());
        return this;
    }
}