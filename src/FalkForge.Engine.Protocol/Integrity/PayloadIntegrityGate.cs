namespace FalkForge.Engine.Integrity;

using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Runtime gate that proves payload integrity <b>and authorship</b> before any package executes.
///
/// <para><b>What the gate proves.</b> The bundle manifest carries a signature envelope over the
/// per-package SHA-256 hashes, each signature self-describing its key. This gate accepts the envelope
/// only when at least one signature verifies <i>and</i> its key's fingerprint is in the engine's
/// baked trusted set (<c>BakedTrustedKeys</c>, surfaced via <see cref="TrustPolicy"/>). That
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
    /// The trust inputs: the pinned fingerprint set (authorship), whether a signature is required, and
    /// the PQ-hybrid companion pins (Stage 1).
    /// </param>
    /// <param name="onPqClassicalFallback">
    /// Loud-log sink for the incapable-OS classical-fallback branch (a hybrid-pinned key accepted on
    /// its classical signature alone because the OS cannot verify ML-DSA). The caller owns making it
    /// visible — ApplyStep forwards it to the UI channel log.
    /// </param>
    /// <returns>
    /// Success when the manifest is unsigned and not required, or when a trusted signature validates,
    /// every signed entry binds to a manifest package whose hash matches, and every manifest package is
    /// covered by the signed set. Returns an <see cref="ErrorKind.IntegrityError"/> otherwise so the
    /// pipeline aborts before a single package runs.
    /// </returns>
    internal static Result<Unit> Verify(
        InstallerManifest manifest, TrustPolicy policy, Action<string>? onPqClassicalFallback = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // Windows payload paths cannot safely distinguish IDs by case. Reject ambiguity
        // even for unsigned bundles, before any consumer resolves an ID to its first match.
        var payloads = new Dictionary<string, string>(StringComparer.Ordinal);
        var payloadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, hash) in EnumeratePayloads(manifest))
        {
            if (string.IsNullOrWhiteSpace(id) || !payloadIds.Add(id))
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT003: Manifest payload ID '{id}' is empty or duplicated.");
            payloads.Add(id, hash);
        }

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
        // key, so refuse rather than accept-any. ApplyStep runs with RequireSigned on the update path
        // (TrustPolicy.RequireSignedUpdate); this guard keeps that path fail-closed. The empty-set
        // consistency-only acceptance stays legal only off the require-signed (fresh-install) path above.
        if (policy.RequireSigned && trustedFingerprints.Count == 0)
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT009: A signature is required on this path but this engine carries no trusted publisher " +
                "keys, so authorship cannot be established. Refusing to accept a signed bundle on trust the " +
                "engine cannot anchor (fail closed).");

        // Anti-downgrade on the update path (C19 quorum uniformity), mirroring SignedPayloadTocVerifier:
        // a signed release older than the highest epoch this machine has accepted is a replay/downgrade.
        // The epoch is part of the signed bytes, so it cannot have been lowered without failing the
        // signature verification below. Fresh installs (IsUpdatePath false) never consult the store.
        if (policy.IsUpdatePath && envelope.Epoch < policy.StoredEpoch)
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                $"INT008: Bundle key-epoch {envelope.Epoch} is below the highest accepted epoch " +
                $"{policy.StoredEpoch} on this machine. Refusing a downgrade/replay of a superseded release.");

        // Authorship + tamper check. Two paths (C19):
        //   - No roles configured  -> the C14 verify-any rule (accept on the first valid trusted signature).
        //     This keeps an un-migrated engine bit-for-bit as C14 (§7.1).
        //   - Roles configured      -> collect ALL valid distinct trusted signatures and evaluate them
        //     against the resolved operation's quorum rule: Install on the fresh-install path, and on the
        //     update path Update (same epoch) or KeyChange (epoch advance) resolved from the signed epoch
        //     relative to the stored epoch — the SAME resolution the staged-update verifier applies, so a
        //     single release key cannot advance the persisted epoch under the weaker Install rule. Fails
        //     loud with INT010 when the policy is unsatisfied.
        // PQ-hybrid Stage 1: the pinned companion map (never read from the bundle) rides into the
        // envelope verifier on both branches below — a hybrid-pinned key needs its ML-DSA companion
        // (INT011 otherwise on a capable OS; classical-fallback + loud log on an incapable one).
        var pqPolicy = policy.CreatePqPolicy(onPqClassicalFallback);

        if (policy.Rules is { } rules && policy.Roles.Count > 0)
        {
            var hasRevocations = envelope.Revoked is { Count: > 0 };
            var operation = BakedTrustPolicy.ResolveOperation(
                policy.IsUpdatePath, envelope.Epoch, policy.StoredEpoch);
            var rule = BakedTrustPolicy.RuleFrom(rules, operation, hasRevocations);
            var roles = policy.Roles;
            var collected = IntegrityEnvelopeCodec.CollectTrustedSignatures(
                envelope, trustedFingerprints,
                fp => roles.TryGetValue(fp, out var r) ? r : TrustRole.Release,
                pqPolicy);
            if (collected.IsFailure)
                return Result<Unit>.Failure(collected.Error);

            var decision = QuorumEvaluator.Evaluate(collected.Value, rule);
            if (!decision.Satisfied)
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT010: The signing quorum for this operation ('{operation}') is not satisfied. {decision.Diagnostic}");
        }
        else
        {
            // An attacker's re-signed bundle (key not in the pinned set) is rejected here with INT001;
            // a hybrid-pinned key with a stripped/invalid PQ companion with INT011.
            var trust = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, trustedFingerprints, revokedFingerprints: null, pqPolicy);
            if (trust.IsFailure)
                return Result<Unit>.Failure(trust.Error);
        }

        // Direction 1 — signed → manifest: every signed entry must bind to a manifest-carried
        // payload whose hash matches the signed hash the cache enforces against payload bytes.
        // The build-time signer covers EVERY embedded payload, so the lookup spans every
        // place the manifest carries one: installable Packages, pre-UI prerequisites
        // (PreUIPackages), the elevation companion (EngineCompanionSha256), and per-package MSI
        // transforms (PackageInfo.Transforms) — the companion executes as SYSTEM and the
        // prerequisites execute too, so a signed entry for any of them must bind, not INT002. A
        // transform is a signed payload but not installable; it binds here yet never joins Packages.
        foreach (var entry in envelope.Files)
        {
            if (string.IsNullOrEmpty(entry.Name))
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    "INT003: Manifest integrity envelope has an entry with an empty name.");

            if (!payloads.TryGetValue(entry.Name, out var manifestHash))
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT002: Signed integrity entry '{entry.Name}' has no matching package in the manifest.");

            if (!string.Equals(manifestHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT002: Integrity hash mismatch for '{entry.Name}'. Signed {entry.Sha256}, manifest has {manifestHash}.");
        }

        // Direction 2 — manifest → signed (set coverage): once a manifest is signed, EVERY
        // package that will execute must be in the signed set. Otherwise an attacker could
        // append an unsigned package to a validly signed bundle and have it run alongside the
        // signed ones. An unsigned-extra package is an IntegrityError, not a silent pass.
        foreach (var id in payloads.Keys)
        {
            if (!IsInSignedSet(envelope, id))
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT004: Manifest payload '{id}' is not covered by the integrity signature. " +
                    "Every payload in a signed manifest must be signed.");
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

    /// <summary>
    /// Enumerates every declared payload for both uniqueness and signature coverage.
    /// Transforms remain payloads of their owning package, never installable packages.
    /// </summary>
    private static IEnumerable<(string Id, string Hash)> EnumeratePayloads(InstallerManifest manifest)
    {
        foreach (var package in manifest.Packages)
        {
            yield return (package.Id, package.Sha256Hash);
            foreach (var transform in package.Transforms)
                yield return (transform.Id, transform.Sha256Hash);
        }

        foreach (var package in manifest.PreUIPackages)
            yield return (package.Id, package.Sha256Hash);

        if (manifest.EngineCompanionSha256 is { } companionHash)
            yield return (FalkForge.Engine.Protocol.Bundle.EngineCompanionPayload.PackageId, companionHash);

        if (manifest.EngineUiSha256 is { } uiHash)
            yield return (FalkForge.Engine.Protocol.Bundle.UiPayload.PackageId, uiHash);
    }
}
