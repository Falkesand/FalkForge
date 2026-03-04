using FalkForge.Extensibility;

namespace FalkForge.Extensions.DotNet;

public sealed class DotNetExtension : IFalkForgeExtension
{
    public string Name => "DotNet";

    public void Register(IExtensionRegistry registry)
    {
        // DotNet extension provides detection capabilities only.
        // It does not contribute MSI tables or components.
        // Detection results are populated as variables by the engine
        // via DotNetDetector during the detect phase.
    }

    public DotNetCoreSearchBuilder SearchForRuntime()
    {
        return new DotNetCoreSearchBuilder();
    }
}
