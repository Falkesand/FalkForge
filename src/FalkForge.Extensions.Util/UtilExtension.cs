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
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would modify XML configuration file(s)" },
                new DryRunAction { Kind = DryRunActionKind.Service,     Description = "Would create local user account(s)" },
                new DryRunAction { Kind = DryRunActionKind.Network,     Description = "Would create file share(s)" },
                new DryRunAction { Kind = DryRunActionKind.Custom,      Description = "Would execute quiet process(es) (QuietExec)" },
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would remove folder(s) on uninstall (RemoveFolderEx)" },
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would create internet shortcut(s)" }
            ],
            DryRunIntent.Uninstall =>
            [
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would restore XML configuration file(s)" },
                new DryRunAction { Kind = DryRunActionKind.Service,     Description = "Would remove local user account(s)" },
                new DryRunAction { Kind = DryRunActionKind.Network,     Description = "Would remove file share(s)" },
                new DryRunAction { Kind = DryRunActionKind.Custom,      Description = "Would execute quiet process(es) (QuietExec rollback)" },
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would remove registered folder(s) (RemoveFolderEx)" },
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would remove internet shortcut(s)" }
            ],
            _ => []
        };

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(_xmlConfigContributor);
    }
}
