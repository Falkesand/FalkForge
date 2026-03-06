using FalkForge.Models;

namespace FalkForge.Compiler.Msix;

public sealed class MsixBundleModel
{
    public required string Name { get; init; }
    public required string Publisher { get; init; }
    public required Version Version { get; init; }
    public IReadOnlyList<MsixBundlePackage> Packages { get; init; } = [];
    public SigningOptions? Signing { get; init; }
}
