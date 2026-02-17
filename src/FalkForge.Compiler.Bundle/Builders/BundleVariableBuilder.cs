namespace FalkForge.Compiler.Bundle.Builders;

public sealed class BundleVariableBuilder
{
    private readonly string _name;
    private BundleVariableType _type = BundleVariableType.String;
    private string? _defaultValue;
    private bool _persisted;
    private bool _hidden;
    private bool _secret;

    internal BundleVariableBuilder(string name)
    {
        _name = name;
    }

    public BundleVariableBuilder String() { _type = BundleVariableType.String; return this; }
    public BundleVariableBuilder Numeric() { _type = BundleVariableType.Numeric; return this; }
    public BundleVariableBuilder Version() { _type = BundleVariableType.Version; return this; }
    public BundleVariableBuilder Default(string value) { _defaultValue = value; return this; }
    public BundleVariableBuilder Persisted() { _persisted = true; return this; }
    public BundleVariableBuilder Hidden() { _hidden = true; return this; }
    public BundleVariableBuilder Secret() { _secret = true; return this; }

    internal BundleVariableModel Build()
    {
        return new BundleVariableModel(
            Name: _name,
            Type: _type,
            DefaultValue: _defaultValue,
            Persisted: _persisted,
            Hidden: _hidden || _secret,
            Secret: _secret
        );
    }
}
