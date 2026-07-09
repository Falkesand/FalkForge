namespace FalkForge.Engine.Protocol.Integrity;

using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Binds the payload bytes a bundle will actually extract and execute to the ECDSA-signed manifest
/// hash. This closes the "signed bundle, TOC-hash tamper" hole.
///
/// <para><b>The gap this closes.</b> At build time one SHA-256 per payload is written to three
/// places: the manifest's <see cref="PackageInfo.Sha256Hash"/> (covered by the signature), the
/// signed integrity envelope, and the appended-overlay <see cref="TocEntry.Sha256Hash"/>. The
/// ECDSA manifest signature covers the first two; it does <b>not</b> cover the overlay TOC (the
/// TOC and payloads are appended after signing and are excluded from Authenticode by construction).
/// Runtime extraction (<see cref="BundleReader"/> / <c>DeltaApplicator</c>) verifies each payload's
/// bytes only against the <b>TOC</b> hash. An attacker can therefore flip payload bytes, recompute
/// the matching TOC hash in the unsigned overlay, and leave the signed manifest and its signature
/// untouched — extraction verifies the tampered bytes against the tampered TOC hash, accepts them,
/// and the payload executes. The signature verifies fine the whole time.</para>
///
/// <para><b>The binding.</b> Before any payload is extracted, this gate requires the value the
/// extractor will trust to equal the signed hash for the same package:
/// <list type="bullet">
///   <item><description>Full payload — <see cref="TocEntry.Sha256Hash"/> (the extractor verifies
///   the decompressed bytes against it) must equal the signed hash. bytes == TOC == signed.</description></item>
///   <item><description>Delta payload — <see cref="TocEntry.ReconstructedSha256Hash"/> (the
///   reconstruction is verified against it) must equal the signed hash. The delta-blob hash
///   (<see cref="TocEntry.Sha256Hash"/>) is unsigned and irrelevant to trust — only the finished
///   reconstruction matters. reconstructed == ReconstructedSha256Hash == signed.</description></item>
/// </list>
/// A TOC hash that disagrees with the signed manifest is a post-signing overlay tamper and is
/// rejected with a <see cref="ErrorKind.SecurityError"/> before a byte is extracted.</para>
///
/// <para><b>Unsigned bundles.</b> When the manifest carries no signature there is no signed hash to
/// bind to, so the gate passes through (backward compatible, matching
/// <c>PayloadIntegrityGate</c>). Such bundles retain only TOC-level tamper detection; producing an
/// integrity-signed bundle (<c>BundleBuilder.Integrity(...)</c>) is what activates this binding.</para>
///
/// <para><b>Payloads outside the signed set.</b> TOC payloads with no matching signed package (the
/// bundle's own UI/engine infrastructure binaries) are outside this binding's scope and are left to
/// TOC-level verification. Binding them would require the signature to cover those payloads, which
/// is a separate change to signature scope.</para>
/// </summary>
public static class SignedPayloadTocVerifier
{
    /// <summary>
    /// Verifies that every TOC payload which is covered by the manifest's ECDSA signature carries a
    /// hash that matches the signed hash, so tampered bytes with a rewritten (unsigned) TOC hash are
    /// rejected before extraction.
    /// </summary>
    /// <param name="manifest">The manifest whose signature envelope (if any) binds the payload hashes.</param>
    /// <param name="tocEntries">The overlay table-of-contents entries the extractor will trust.</param>
    /// <param name="trustedFingerprints">
    /// The engine's pinned trusted-key fingerprints (uppercase hex). A signature is accepted only when
    /// its key's fingerprint is in this set (verify-any across a dual-signed envelope). An empty set
    /// means consistency-only mode (no baked publisher — accept any self-verifying signature), used by
    /// an unconfigured engine.
    /// </param>
    /// <param name="requireSigned">
    /// When true, an unsigned manifest is rejected (INT007) instead of passed through. Stage 1 fresh
    /// installs pass false (backward compatible); the require-signed update path is Stage 2.
    /// </param>
    public static Result<Unit> Verify(
        InstallerManifest manifest,
        IReadOnlyList<TocEntry> tocEntries,
        IReadOnlySet<string> trustedFingerprints,
        bool requireSigned = false)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tocEntries);
        ArgumentNullException.ThrowIfNull(trustedFingerprints);

        // Unsigned manifest: no signed hash exists to bind to. A present-but-untrusted signature is an
        // attack signal (rejected below); a wholly absent one is a legacy/unsigned bundle. Fresh
        // installs pass through for backward compatibility; the update path (Stage 2) sets requireSigned.
        if (manifest.ManifestSignature is null)
        {
            if (requireSigned)
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    "INT007: A signature is required on this path but the manifest carries none. " +
                    "Refusing to extract or execute an unsigned payload.");
            return Unit.Value;
        }

        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
        if (envelope is null)
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT003: Failed to parse manifest integrity envelope.");

        // The signed hashes are only trustworthy if a TRUSTED signature verifies. A tampered, forged,
        // or attacker-re-signed envelope (key not in the pinned set) is rejected here, before any hash
        // from it is trusted.
        var trust = IntegrityEnvelopeCodec.VerifyTrusted(envelope, trustedFingerprints);
        if (trust.IsFailure)
            return trust;

        // Signed hash per package id — the ECDSA-covered source of truth for payload integrity.
        var signedHashes = new Dictionary<string, string>(envelope.Files.Count, StringComparer.Ordinal);
        foreach (var file in envelope.Files)
        {
            if (!string.IsNullOrEmpty(file.Name))
                signedHashes[file.Name] = file.Sha256;
        }

        foreach (var entry in tocEntries)
        {
            // Payloads with no matching signed package (UI/engine infrastructure) are outside this
            // binding's scope — left to TOC-level verification. See the type remarks.
            if (!signedHashes.TryGetValue(entry.PackageId, out var signedHash))
                continue;

            // The value the runtime extractor verifies payload bytes against MUST equal the signed
            // hash. For a delta payload that is the reconstructed-file hash, not the delta-blob hash.
            var boundHash = entry.IsDelta ? entry.ReconstructedSha256Hash : entry.Sha256Hash;

            if (string.IsNullOrEmpty(boundHash)
                || !string.Equals(boundHash, signedHash, StringComparison.OrdinalIgnoreCase))
            {
                var which = entry.IsDelta ? "reconstructed payload " : "payload ";
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT006: {which}hash for '{entry.PackageId}' in the bundle table of contents " +
                    $"({boundHash ?? "<none>"}) does not match the ECDSA-signed manifest hash " +
                    $"({signedHash}). The bundle's payload table was tampered with after signing — " +
                    "refusing to extract or execute the payload.");
            }
        }

        return Unit.Value;
    }
}
