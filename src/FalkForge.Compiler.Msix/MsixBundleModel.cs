using FalkForge.Models;

namespace FalkForge.Compiler.Msix;

public sealed class MsixBundleModel
{
    public required string Name { get; init; }

    // No Publisher: IAppxBundleWriter is given only a version, and derives the bundle identity's
    // Name and Publisher from the payload packages added to it. There is nowhere for a
    // caller-supplied publisher to go.
    public required Version Version { get; init; }
    public IReadOnlyList<MsixBundlePackage> Packages { get; init; } = [];
    public SigningOptions? Signing { get; init; }
}
