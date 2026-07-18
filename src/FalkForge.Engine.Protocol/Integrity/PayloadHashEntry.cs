namespace FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// A payload identifier paired with its expected SHA-256 hash, fed to
/// <see cref="EcdsaManifestSigner"/> to build the signed integrity envelope.
///
/// <para>Shared by both build-time signers: the bundle compiler's payload list and the MSI
/// compiler's resolved-file list both reduce to this same shape before signing, so
/// <see cref="EcdsaManifestSigner"/> has exactly one signing code path for both artifact types.</para>
/// </summary>
/// <param name="PackageId">The package/payload id, or file name for MSI; becomes the entry name.</param>
/// <param name="Sha256">The uppercase hex SHA-256 of the payload bytes.</param>
public readonly record struct PayloadHashEntry(string PackageId, string Sha256);
