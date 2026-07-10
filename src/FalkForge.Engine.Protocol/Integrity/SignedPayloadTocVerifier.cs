namespace FalkForge.Engine.Protocol.Integrity;

using System.Collections.Frozen;
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
    private static readonly FrozenDictionary<string, TrustRole> EmptyRoles =
        FrozenDictionary<string, TrustRole>.Empty;

    // Returns the collected signatures with any locally-revoked fingerprint removed. Allocation-free when
    // there are no revocations (the common path) — returns the input list unchanged.
    private static IReadOnlyList<TrustedSignature> DropRevoked(
        IReadOnlyList<TrustedSignature> collected, IReadOnlySet<string>? revokedFingerprints)
    {
        if (revokedFingerprints is null || revokedFingerprints.Count == 0 || collected.Count == 0)
            return collected;

        var kept = new List<TrustedSignature>(collected.Count);
        foreach (var signature in collected)
        {
            if (!revokedFingerprints.Contains(signature.Fingerprint))
                kept.Add(signature);
        }

        return kept;
    }

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
    /// When true, an unsigned manifest is rejected (INT007) instead of passed through, AND a present
    /// signature with no trust anchor (an empty <paramref name="trustedFingerprints"/> set) is rejected
    /// (INT009, fail closed) rather than accepted consistency-only. Stage 1 fresh installs pass false
    /// (backward compatible); the require-signed update path is Stage 2/3.
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
    /// <param name="policyTable">
    /// The C19 per-operation quorum table to enforce (typically <see cref="BakedTrustPolicy.Default"/>).
    /// Null (the default) keeps the C14 verify-any path — accept on the first valid trusted signature — so
    /// every existing caller and already-signed bundle verifies exactly as before. Non-null is supplied only
    /// on the update path, where the operation is resolved from the signed epoch relative to
    /// <paramref name="storedEpoch"/> (§5.3) and the collected distinct signatures are evaluated against the
    /// resolved rule, failing with INT010 when unsatisfied.
    /// </param>
    /// <param name="roles">
    /// Resolves each accepted fingerprint to its pinned role(s) for the quorum evaluation. Only consulted
    /// when <paramref name="policyTable"/> is non-null; a fingerprint absent from the map defaults to
    /// <see cref="TrustRole.Release"/>.
    /// </param>
    public static Result<Unit> Verify(
        InstallerManifest manifest,
        IReadOnlyList<TocEntry> tocEntries,
        IReadOnlySet<string> trustedFingerprints,
        bool requireSigned = false,
        int storedEpoch = 0,
        IReadOnlySet<string>? revokedFingerprints = null,
        IReadOnlyDictionary<OperationKind, PolicyRule>? policyTable = null,
        IReadOnlyDictionary<string, TrustRole>? roles = null)
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

        // Fail closed on the require-signed path when there is no trust anchor (C14 Stage 3 FIX 2 / B1).
        // An empty trusted set means "no baked publisher key", which makes MatchTrustedSignature fall back
        // to consistency-only (accept ANY self-verifying signature). On the update path that is fail-open:
        // an attacker re-signs a rewritten update with their own fresh key and it would be accepted.
        // Require-signed cannot establish authorship without a pinned key, so refuse rather than accept-any.
        // The empty-set consistency-only acceptance stays legal only on the NON-require-signed
        // (fresh-install / inspection-grade) path handled below.
        if (requireSigned && trustedFingerprints.Count == 0)
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT009: A signature is required on this path but this engine carries no trusted publisher " +
                "keys, so authorship cannot be established. Refusing to accept a signed update on trust the " +
                "engine cannot anchor (fail closed).");

        if (policyTable is null)
        {
            // C14 verify-any path. The signed hashes are only trustworthy if a TRUSTED signature
            // verifies. A tampered, forged, or attacker-re-signed envelope (key not in the pinned
            // set) is rejected here. Revocation (§6.3 step 3) is enforced INSIDE the match: a
            // locally-revoked key is skipped rather than fatal, so a dual-signed rotation bundle
            // [revoked-old, good-new] is still accepted via its non-revoked signature (matching
            // the quorum path's DropRevoked), while a bundle with ONLY revoked trusted signatures
            // fails INT001.
            var trust = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, trustedFingerprints, revokedFingerprints);
            if (trust.IsFailure)
                return Result<Unit>.Failure(trust.Error);

            // Anti-downgrade (§6.3 step 2): a signed release older than the highest epoch this machine has
            // accepted is a replay/downgrade. The epoch is part of the signed bytes, so it cannot have been
            // lowered without failing the verify above.
            if (envelope.Epoch < storedEpoch)
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT008: Bundle key-epoch {envelope.Epoch} is below the highest accepted epoch " +
                    $"{storedEpoch} on this machine. Refusing a downgrade/replay of a superseded release.");
        }
        else
        {
            // C19 quorum path. Anti-downgrade first (INT008), so a revoked-key replay cannot even reach the
            // quorum count.
            if (envelope.Epoch < storedEpoch)
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT008: Bundle key-epoch {envelope.Epoch} is below the highest accepted epoch " +
                    $"{storedEpoch} on this machine. Refusing a downgrade/replay of a superseded release.");

            // The quorum table is supplied only on the update path, so resolve within the Update family:
            // a signed epoch above the stored epoch is a rotation (KeyChange), otherwise a routine Update
            // (§5.3). A below-stored epoch is already rejected by INT008 above.
            var hasRevocations = envelope.Revoked is { Count: > 0 };
            var operation = BakedTrustPolicy.ResolveOperation(
                isUpdatePath: true, envelope.Epoch, storedEpoch);
            var rule = BakedTrustPolicy.RuleFrom(policyTable, operation, hasRevocations);

            var roleMap = roles ?? EmptyRoles;
            var collected = IntegrityEnvelopeCodec.CollectTrustedSignatures(
                envelope, trustedFingerprints,
                fp => roleMap.TryGetValue(fp, out var r) ? r : TrustRole.Release);
            if (collected.IsFailure)
                return Result<Unit>.Failure(collected.Error);

            // Drop locally-revoked keys before counting toward the threshold (§6.2 step 5): a revoked key
            // can never contribute to a quorum even if it is still in the baked trusted set.
            var usable = DropRevoked(collected.Value, revokedFingerprints);

            var decision = QuorumEvaluator.Evaluate(usable, rule);
            if (!decision.Satisfied)
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT010: The signing quorum for this update ('{operation}') is not satisfied. {decision.Diagnostic}");
        }

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
