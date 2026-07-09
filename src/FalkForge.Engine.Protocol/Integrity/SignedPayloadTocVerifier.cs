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
/// <para><b>Coverage (§5.4).</b> Once a bundle is signed, every payload in its table of contents must
/// be inside the signed set — the bundle's own UI/engine binaries included, because those execute. A
/// TOC entry with no matching signed package is treated as an attacker-appended payload (e.g. a
/// malicious UI executable the bootstrapper would launch) and rejected with INT004. The build-time
/// signer already signs every embedded payload, so a legitimately signed bundle has full coverage;
/// only a post-signing append fails here.</para>
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
    /// <param name="storedEpoch">
    /// The highest key-epoch this machine has already accepted (from the persisted trust store, §6.2).
    /// A bundle whose signed epoch is below this is a downgrade/replay and is rejected (INT008). Defaults
    /// to 0 (no anti-downgrade — fresh install and inspection-grade CLI paths pass 0).
    /// </param>
    /// <param name="revokedFingerprints">
    /// Fingerprints recorded as revoked in the persisted store (§6.3 step 3). A bundle whose accepted
    /// signature is one of these is rejected (INT001) even if the key is still in the baked trusted set.
    /// Null/empty means no local revocations are enforced.
    /// </param>
    public static Result<Unit> Verify(
        InstallerManifest manifest,
        IReadOnlyList<TocEntry> tocEntries,
        IReadOnlySet<string> trustedFingerprints,
        bool requireSigned = false,
        int storedEpoch = 0,
        IReadOnlySet<string>? revokedFingerprints = null)
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
        // from it is trusted. MatchTrustedSignature returns the accepted fingerprint so we can enforce
        // the persisted revocation list against it.
        var trust = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, trustedFingerprints);
        if (trust.IsFailure)
            return Result<Unit>.Failure(trust.Error);

        // Anti-downgrade (§6.3 step 2): a signed release older than the highest epoch this machine has
        // accepted is a replay/downgrade (e.g. a bundle signed by a since-revoked key). The epoch is part
        // of the signed bytes, so it cannot have been lowered without failing the verify above.
        if (envelope.Epoch < storedEpoch)
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                $"INT008: Bundle key-epoch {envelope.Epoch} is below the highest accepted epoch " +
                $"{storedEpoch} on this machine. Refusing a downgrade/replay of a superseded release.");

        // Revocation (§6.3 step 3): the accepted key is still in the baked trusted set but has been
        // recorded as revoked locally (via a previously-applied update). The revocation overrides the
        // stale baked trust.
        if (revokedFingerprints is not null && revokedFingerprints.Contains(trust.Value))
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT001: The bundle's signature is from a key that has been revoked on this machine. " +
                "Refusing to extract or execute a payload signed by a revoked publisher key.");

        // Signed hash per package id — the ECDSA-covered source of truth for payload integrity.
        var signedHashes = new Dictionary<string, string>(envelope.Files.Count, StringComparer.Ordinal);
        foreach (var file in envelope.Files)
        {
            if (!string.IsNullOrEmpty(file.Name))
                signedHashes[file.Name] = file.Sha256;
        }

        foreach (var entry in tocEntries)
        {
            // Coverage extension (§5.4): once a bundle is signed, EVERY payload it will extract and
            // (potentially) execute must be inside the signed set. A TOC entry with no matching signed
            // package is an attacker-appended payload — e.g. a malicious UI executable that the
            // bootstrapper would launch, or an extra chained package — that the signature never covered.
            // The former "skip unmatched" behavior left exactly that hole; reject it instead.
            if (!signedHashes.TryGetValue(entry.PackageId, out var signedHash))
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT004: Bundle payload '{entry.PackageId}' is present in the table of contents but is " +
                    "not covered by the integrity signature. Every payload a signed bundle extracts or " +
                    "executes must be signed — refusing to extract or execute an uncovered payload.");

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
