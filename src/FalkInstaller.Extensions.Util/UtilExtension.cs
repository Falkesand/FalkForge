using FalkInstaller.Extensibility;
using FalkInstaller.Extensions.Util.XmlConfig;

namespace FalkInstaller.Extensions.Util;

public sealed class UtilExtension : IFalkInstallerExtension
{
    private readonly XmlConfigTableContributor _xmlConfigContributor = new();

    public string Name => "Util";

    public XmlConfigTableContributor XmlConfig => _xmlConfigContributor;

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(_xmlConfigContributor);
    }
}
