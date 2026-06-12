namespace FalkForge.Engine.Integrity;

using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Runtime gate that proves payload integrity before any package executes.
///
/// <para>The bundle manifest carries an ECDSA signature envelope over the per-package
/// SHA-256 hashes. The cache layer already verifies each payload's bytes against its
/// <see cref="PackageInfo.Sha256Hash"/>; this gate proves those hashes are the ones the
/// builder signed. Together they detect tampering even when an attacker rewrites both a
/// payload and its manifest hash — the attacker cannot forge the ECDSA signature without
/// the private key.</para>
///
/// <para>Verification is independent of Authenticode: it works for unsigned (in the
/// Authenticode sense) payloads and uses only built-in .NET cryptography, so the NativeAOT
/// engine needs no external tool. An unsigned manifest passes through unchanged for
/// backward compatibility.</para>
/// </summary>
internal static class PayloadIntegrityGate
{
    /// <summary>
    /// Verifies the manifest's integrity envelope, if present. Returns success when the
    /// manifest is unsigned (backward compatible) or when the signature is valid and every
    /// signed entry binds to a manifest package whose hash matches. Returns a
    /// <see cref="ErrorKind.SecurityError"/> otherwise so the pipeline aborts the install.
    /// </summary>
    internal static Result<Unit> Verify(InstallerManifest manifest)
    {
        if (manifest.ManifestSignature is null)
            return Result<Unit>.Success(default);

        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
        if (envelope is null)
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "INT003: Failed to parse manifest integrity envelope.");

        if (string.IsNullOrEmpty(envelope.PublicKey) || string.IsNullOrEmpty(envelope.Signature))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "INT003: Manifest integrity envelope is missing the public key or signature.");

        if (!IntegrityEnvelopeCodec.VerifySignature(envelope))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "INT001: Manifest integrity signature verification failed. The installer may have been tampered with.");

        // Bind each signed entry to its manifest package and confirm the signed hash
        // matches the package hash the cache enforces against payload bytes.
        foreach (var entry in envelope.Files)
        {
            if (string.IsNullOrEmpty(entry.Name))
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    "INT003: Manifest integrity envelope has an entry with an empty name.");

            var package = FindPackage(manifest, entry.Name);
            if (package is null)
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    $"INT002: Signed integrity entry '{entry.Name}' has no matching package in the manifest.");

            if (!string.Equals(package.Sha256Hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    $"INT002: Integrity hash mismatch for '{entry.Name}'. Signed {entry.Sha256}, manifest has {package.Sha256Hash}.");
        }

        return Result<Unit>.Success(default);
    }

    private static PackageInfo? FindPackage(InstallerManifest manifest, string id)
    {
        foreach (var package in manifest.Packages)
        {
            if (string.Equals(package.Id, id, StringComparison.Ordinal))
                return package;
        }

        return null;
    }
}
