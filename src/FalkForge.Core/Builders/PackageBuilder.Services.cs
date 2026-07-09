namespace FalkForge.Builders;

// Windows service installation and control.
public sealed partial class PackageBuilder
{
    public PackageBuilder Service(string name, Action<ServiceBuilder> configure)
    {
        var builder = new ServiceBuilder(name);
        configure(builder);
        _services.Add(builder.Build());
        return this;
    }

    public PackageBuilder ServiceControl(Action<ServiceControlBuilder> configure)
    {
        var builder = new ServiceControlBuilder();
        configure(builder);
        _serviceControls.Add(builder.Build());
        return this;
    }
}
