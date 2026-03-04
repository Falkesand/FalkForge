namespace FalkForge.Extensions.Dependency;

public sealed class DependencyProviderBuilder
{
    private readonly string _key;
    private string? _componentRef;
    private string? _displayName;
    private string _version = string.Empty;

    internal DependencyProviderBuilder(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _key = key;
    }

    public DependencyProviderBuilder Version(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _version = version;
        return this;
    }

    public DependencyProviderBuilder DisplayName(string displayName)
    {
        _displayName = displayName;
        return this;
    }

    public DependencyProviderBuilder ComponentRef(string componentRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentRef);
        _componentRef = componentRef;
        return this;
    }

    internal DependencyProviderModel Build()
    {
        return new DependencyProviderModel
        {
            Key = _key,
            Version = _version,
            DisplayName = _displayName,
            ComponentRef = _componentRef
        };
    }
}