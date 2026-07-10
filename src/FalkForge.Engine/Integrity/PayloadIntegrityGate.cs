namespace FalkForge.Engine.Integrity;

using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Runtime gate that proves payload integrity <b>and authorship</b> before any package executes.
///
/// <para><b>What the gate proves.</b> The bundle manifest carries a signature envelope over the
/// per-package SHA-256 hashes, each signature self-describing its key. This gate accepts the envelope
/// only when at least one signature verifies <i>and</i> its key's fingerprint is in the engine's
/// baked trusted set (<see cref="BakedTrustedKeys"/>, surfaced via <see cref="TrustPolicy"/>). That
/// closes the re-sign attack: an attacker who rewrites the bundle and re-signs with their own key is
/// rejected because their fingerprint is not pinned. It then binds every signed entry to a manifest
/// package with a matching hash, and requires every executing package to be in the signed set. The
/// binding of the actual payload <i>bytes</i> to the signed hash happens at extraction time in
/// <see cref="SignedPayloadTocVerifier"/>.</para>
///
/// <para><b>Unpinned engines.</b> When the trusted set is empty (an engine built with no publisher
/// key), verification falls back to consistency-only: any self-verifying signature is accepted
/// (tamper-evidence, not authorship). An unsigned manifest passes through unless
/// <see cref="TrustPolicy.RequireSigned"/> is set (Stage 2 update path). On the require-signed path an
/// empty set is instead rejected (INT009, fail closed) — a required signature with no trust anchor
/// cannot establish authorship, so it is refused rather than accepted consistency-only.</para>
///
/// <para>Verification is independent of Authenticode and uses only built-in .NET cryptography, so the
/// NativeAOT engine needs no external tool.</para>
/// </summary>
internal static class PayloadIntegrityGate
{
    /// <summary>
    /// Verifies the manifest's integrity envelope against the supplied trust policy.
    /// </summary>
    /// <param name="manifest">The manifest whose signature envelope (if any) is verified.</param>
    /// <param name="policy">
    /// The trust inputs: the pinned fingerprint set (authorship) and whether a signature is required.
    /// </param>
    /// <returns>
    /// Success when the manifest is unsigned and not required, or when a trusted signature validates,
    /// every signed entry binds to a manifest package whose hash matches, and every manifest package is
    /// covered by the signed set. Returns an <see cref="ErrorKind.IntegrityError"/> otherwise so the
    /// pipeline aborts before a single package runs.
    /// </returns>
    internal static Result<Unit> Verify(InstallerManifest manifest, TrustPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var trustedFingerprints = policy.TrustedFingerprints ?? TrustPolicy.ConsistencyOnly.TrustedFingerprints;

        if (manifest.ManifestSignature is null)
        {
            // A wholly absent signature is a legacy/unsigned bundle. Fresh installs pass through; the
            // require-signed update path (Stage 2) rejects it.
            if (policy.RequireSigned)
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    "INT007: A signature is required on this path but the manifest carries none. " +
                    "Refusing to install an unsigned bundle.");
            return Result<Unit>.Success(default);
        }

        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
        if (envelope is null)
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT003: Failed to parse manifest integrity envelope.");

        // Fail closed on the require-signed path when there is no trust anchor (mirrors
        // SignedPayloadTocVerifier's INT009 guard; C14 Stage 3 FIX 2 / B1). An empty trusted set makes
        // VerifyTrusted fall back to consistency-only (accept ANY self-verifying signature). On a
        // require-signed path that is fail-open: an attacker re-signs a rewritten update with their own
        // fresh key and it would be accepted. Require-signed cannot establish authorship without a pinned
        // key, so refuse rather than accept-any. ApplyStep never sets RequireSigned today (so this is no
        // live behavior change), but the guard prevents a future silent fail-open. The empty-set
        // consistency-only acceptance stays legal only off the require-signed (fresh-install) path above.
        if (policy.RequireSigned && trustedFingerprints.Count == 0)
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT009: A signature is required on this path but this engine carries no trusted publisher " +
                "keys, so authorship cannot be established. Refusing to accept a signed bundle on trust the " +
                "engine cannot anchor (fail closed).");

        // Authorship + tamper check. Two paths (C19):
        //   - No roles configured  -> the C14 verify-any rule (accept on the first valid trusted signature).
        //     This keeps an un-migrated engine bit-for-bit as C14 (§7.1).
        //   - Roles configured      -> collect ALL valid distinct trusted signatures and evaluate them
        //     against the Install operation's quorum rule (a fresh install is always the Install operation),
        //     failing loud with INT010 when the policy is unsatisfied.
        if (policy.Rules is { } rules && policy.Roles.Count > 0)
        {
            var hasRevocations = envelope.Revoked is { Count: > 0 };
            var rule = BakedTrustPolicy.RuleFrom(rules, OperationKind.Install, hasRevocations);
            var roles = policy.Roles;
            var collected = IntegrityEnvelopeCodec.CollectTrustedSignatures(
                envelope, trustedFingerprints,
                fp => roles.TryGetValue(fp, out var r) ? r : TrustRole.Release);
            if (collected.IsFailure)
                return Result<Unit>.Failure(collected.Error);

            var decision = QuorumEvaluator.Evaluate(collected.Value, rule);
            if (!decision.Satisfied)
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT010: The signing quorum for a fresh install is not satisfied. {decision.Diagnostic}");
        }
        else
        {
            // An attacker's re-signed bundle (key not in the pinned set) is rejected here with INT001.
            var trust = IntegrityEnvelopeCodec.VerifyTrusted(envelope, trustedFingerprints);
            if (trust.IsFailure)
                return trust;
        }

        // Direction 1 — signed → manifest: every signed entry must bind to a manifest package
        // whose hash matches the signed hash the cache enforces against payload bytes.
        foreach (var entry in envelope.Files)
        {
            if (string.IsNullOrEmpty(entry.Name))
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    "INT003: Manifest integrity envelope has an entry with an empty name.");

            var package = FindPackage(manifest, entry.Name);
            if (package is null)
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT002: Signed integrity entry '{entry.Name}' has no matching package in the manifest.");

            if (!string.Equals(package.Sha256Hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT002: Integrity hash mismatch for '{entry.Name}'. Signed {entry.Sha256}, manifest has {package.Sha256Hash}.");
        }

        // Direction 2 — manifest → signed (set coverage): once a manifest is signed, EVERY
        // package that will execute must be in the signed set. Otherwise an attacker could
        // append an unsigned package to a validly signed bundle and have it run alongside the
        // signed ones. An unsigned-extra package is an IntegrityError, not a silent pass.
        foreach (var package in manifest.Packages)
        {
            if (!IsInSignedSet(envelope, package.Id))
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT004: Manifest package '{package.Id}' is not covered by the integrity signature. " +
                    "Every package in a signed manifest must be signed.");
        }

        return Result<Unit>.Success(default);
    }

    private static bool IsInSignedSet(ManifestSignatureEnvelope envelope, string packageId)
    {
        foreach (var entry in envelope.Files)
        {
            if (string.Equals(entry.Name, packageId, StringComparison.Ordinal))
                return true;
        }

        return false;
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
