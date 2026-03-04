namespace FalkForge.Extensions.Dependency;

public sealed class DependencyConsumerBuilder
{
    private readonly string _providerKey;
    private string? _componentRef;
    private string _consumerKey = string.Empty;
    private bool _maxInclusive;
    private string? _maxVersion;
    private bool _minInclusive = true;
    private string? _minVersion;

    internal DependencyConsumerBuilder(string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        _providerKey = providerKey;
    }

    public DependencyConsumerBuilder ConsumerKey(string consumerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        _consumerKey = consumerKey;
        return this;
    }

    public DependencyConsumerBuilder MinVersion(string minVersion)
    {
        _minVersion = minVersion;
        return this;
    }

    public DependencyConsumerBuilder MaxVersion(string maxVersion)
    {
        _maxVersion = maxVersion;
        return this;
    }

    public DependencyConsumerBuilder MinInclusive()
    {
        _minInclusive = true;
        return this;
    }

    public DependencyConsumerBuilder MinExclusive()
    {
        _minInclusive = false;
        return this;
    }

    public DependencyConsumerBuilder MaxInclusive()
    {
        _maxInclusive = true;
        return this;
    }

    public DependencyConsumerBuilder MaxExclusive()
    {
        _maxInclusive = false;
        return this;
    }

    public DependencyConsumerBuilder ComponentRef(string componentRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentRef);
        _componentRef = componentRef;
        return this;
    }

    internal DependencyConsumerModel Build()
    {
        return new DependencyConsumerModel
        {
            ProviderKey = _providerKey,
            ConsumerKey = _consumerKey,
            MinVersion = _minVersion,
            MaxVersion = _maxVersion,
            MinInclusive = _minInclusive,
            MaxInclusive = _maxInclusive,
            ComponentRef = _componentRef
        };
    }
}