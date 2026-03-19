namespace FalkForge.Compiler.Msix;

public sealed class MsixBundlePackage
{
    public required string FilePath { get; init; }
    public required ProcessorArchitecture Architecture { get; init; }
}
