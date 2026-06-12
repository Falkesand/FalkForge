namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// A payload identifier paired with its expected SHA-256 hash, fed to
/// <see cref="EcdsaManifestSigner"/> to build the signed integrity envelope.
/// </summary>
/// <param name="PackageId">The bundle package id; becomes the entry name.</param>
/// <param name="Sha256">The uppercase hex SHA-256 of the payload bytes.</param>
public readonly record struct PayloadHashEntry(string PackageId, string Sha256);
