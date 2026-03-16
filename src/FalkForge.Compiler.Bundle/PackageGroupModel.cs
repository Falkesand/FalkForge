namespace FalkForge.Compiler.Bundle;

/// <summary>
/// A reusable group of packages that can be added to a bundle chain.
/// Package groups are flattened at build time -- their packages are
/// inserted directly into the chain in order.
/// </summary>
public sealed class PackageGroupModel
{
    public required string Id { get; init; }
    public IReadOnlyList<BundlePackageModel> Packages { get; init; } = [];
}
