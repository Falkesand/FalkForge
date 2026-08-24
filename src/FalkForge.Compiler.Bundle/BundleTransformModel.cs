namespace FalkForge.Compiler.Bundle;

/// <summary>
/// An MSI transform (.mst) an author declared for a package via
/// <see cref="Builders.BundlePackageBuilder.Transform(string, string)"/>. The compiler embeds
/// the transform's bytes as a signed bundle payload keyed by <see cref="Id"/> and records the
/// package-to-transform association in the signature envelope, so the transform is exactly as
/// trust-covered as the package it belongs to.
/// </summary>
public sealed class BundleTransformModel
{
    /// <summary>The author-chosen id of the transform; becomes the signed payload entry's name.</summary>
    public required string Id { get; init; }

    /// <summary>Path to the transform (.mst) file on disk. Hashed and embedded at compile time.</summary>
    public required string SourcePath { get; init; }
}
