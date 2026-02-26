using FalkForge.Extensibility;

namespace FalkForge.Extensions.DotNet;

public sealed class DotNetExtension : IFalkForgeExtension, IDryRunContributor
{
    public string Name => "DotNet";

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install => [new DryRunAction { Kind = DryRunActionKind.FileSystem, Description = "Would detect .NET runtime via registry and filesystem" }],
            _ => []
        };

    public void Register(IExtensionRegistry registry)
    {
        // DotNet extension provides detection capabilities only.
        // It does not contribute MSI tables or components.
        // Detection results are populated as variables by the engine
        // via DotNetDetector during the detect phase.
    }
}
