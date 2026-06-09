using FalkForge.Models;

namespace FalkForge.Compiler.Msi;

public sealed class ResolvedPackage
{
    public required PackageModel Package { get; init; }
    public required IReadOnlyList<ResolvedComponent> Components { get; init; }
    public required IReadOnlyList<ResolvedFile> Files { get; init; }

    /// <summary>
    ///     Per-instance identifier assigned at construction time.
    ///     Used as a build-nonce for PackageCode derivation in normal (non-reproducible)
    ///     mode: two separate <see cref="ResolvedPackage"/> instances that happen to share
    ///     identical content will still produce different PackageCodes, satisfying the MSI
    ///     requirement that distinct packaging events yield distinct PackageCodes.
    ///     Callers that need a stable PackageCode across multiple <c>MsiRecipeBuilder.Build</c>
    ///     calls should reuse the same <see cref="ResolvedPackage"/> instance.
    /// </summary>
    public Guid InstanceId { get; } = Guid.NewGuid();
}