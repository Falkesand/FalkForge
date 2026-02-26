using FalkForge.Extensibility;
using FalkForge.Extensions.Util.XmlConfig;

namespace FalkForge.Extensions.Util;

public sealed class UtilExtension : IFalkForgeExtension, IDryRunContributor
{
    private readonly XmlConfigTableContributor _xmlConfigContributor = new();

    public string Name => "Util";

    public XmlConfigTableContributor XmlConfig => _xmlConfigContributor;

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install =>
            [
                new DryRunAction { Kind = DryRunActionKind.FileSystem, Description = "Would modify XML configuration file(s)" },
                new DryRunAction { Kind = DryRunActionKind.Service, Description = "Would create local user account(s)" }
            ],
            DryRunIntent.Uninstall =>
            [
                new DryRunAction { Kind = DryRunActionKind.FileSystem, Description = "Would restore XML configuration file(s)" },
                new DryRunAction { Kind = DryRunActionKind.Service, Description = "Would remove local user account(s)" }
            ],
            _ => []
        };

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(_xmlConfigContributor);
    }
}
