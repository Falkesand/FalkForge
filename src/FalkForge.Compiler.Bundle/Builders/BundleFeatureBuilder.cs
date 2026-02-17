namespace FalkForge.Compiler.Bundle.Builders;

public sealed class BundleFeatureBuilder
{
    private readonly string _id;
    private string? _title;
    private string? _description;
    private bool _isDefault = true;
    private bool _isRequired;
    private readonly List<string> _packageIds = new();

    internal BundleFeatureBuilder(string id)
    {
        _id = id;
    }

    public BundleFeatureBuilder Title(string title) { _title = title; return this; }
    public BundleFeatureBuilder Description(string description) { _description = description; return this; }
    public BundleFeatureBuilder Default(bool isDefault) { _isDefault = isDefault; return this; }

    public BundleFeatureBuilder Required()
    {
        _isRequired = true;
        _isDefault = true;
        return this;
    }

    public BundleFeatureBuilder Package(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        _packageIds.Add(packageId);
        return this;
    }

    internal BundleFeatureModel Build()
    {
        return new BundleFeatureModel
        {
            Id = _id,
            Title = _title ?? string.Empty,
            Description = _description,
            IsDefault = _isRequired || _isDefault,
            IsRequired = _isRequired,
            PackageIds = _packageIds.AsReadOnly()
        };
    }
}
