namespace FalkForge.Builders;

// Registry entries/removals, environment variables, and MSI properties.
public sealed partial class PackageBuilder
{
    public PackageBuilder Registry(Action<RegistryBuilder> configure)
    {
        var builder = new RegistryBuilder();
        configure(builder);
        _registryEntries.AddRange(builder.Build());
        return this;
    }

    public PackageBuilder RemoveRegistry(Action<RemoveRegistryBuilder> configure)
    {
        var builder = new RemoveRegistryBuilder();
        configure(builder);
        _removeRegistryEntries.Add(builder.Build());
        return this;
    }

    public PackageBuilder EnvironmentVariable(string name, string value,
        Action<EnvironmentVariableBuilder>? configure = null)
    {
        var builder = new EnvironmentVariableBuilder(name, value);
        configure?.Invoke(builder);
        _environmentVariables.Add(builder.Build());
        return this;
    }

    public PackageBuilder EnvironmentVariable(string name, MsiProperty property,
        Action<EnvironmentVariableBuilder>? configure = null)
    {
        return EnvironmentVariable(name, property.ToString(), configure);
    }

    public PackageBuilder Property(string name, string value, Action<PropertyBuilder>? configure = null)
    {
        var builder = new PropertyBuilder(name, value);
        configure?.Invoke(builder);
        _properties.Add(builder.Build());
        return this;
    }
}
