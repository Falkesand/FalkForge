using FalkForge.Extensibility;
using FalkForge.Extensions.Util.XmlConfig;

namespace FalkForge.Extensions.Util;

public sealed class UtilExtension : IFalkForgeExtension
{
    public XmlConfigTableContributor XmlConfig { get; } = new();

    public string Name => "Util";

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(XmlConfig);
    }
}