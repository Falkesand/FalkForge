namespace FalkForge.Engine.Integrity;

using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// The engine's own bundle-trust decision for the self-extract and bootstrap paths: binds the payloads a
/// bundle will extract/execute to its ECDSA-signed manifest against the engine's effective trusted set
/// (<see cref="EngineTrustAnchor"/>), consulting the caller-supplied persisted trust state
/// (anti-downgrade epoch + local revocations) on the require-signed path.
///
/// <para>Extracted from <c>BootstrapperRunner.RunAsync</c> / <c>EnginePayloadTrust.VerifySignedPayloadTrust</c> so both
/// call sites make the identical decision and the decision itself is unit-testable. The staged-update path
/// (<see cref="StagedUpdateVerifier"/>) applies the same verification over a downloaded artifact before it
/// is ever relaunched.</para>
///
/// <para><b>Quorum uniformity (C19).</b> On the require-signed path this gate threads the engine's
/// effective policy table and roles (<see cref="EngineTrustAnchor.EffectivePolicyTable"/> /
/// <see cref="EngineTrustAnchor.EffectiveRoles"/>) exactly as <see cref="StagedUpdateVerifier"/> does, so
/// the operation is resolved from the signed epoch relative to the stored epoch (same epoch → Update,
/// epoch advance → KeyChange requiring the release+recovery quorum). This matters because a bundle can
/// reach this gate out-of-band (manual run / IT push with <c>--require-signed</c>) without ever passing
/// the staged-update verifier — and a completed apply on that path advances the persisted anti-downgrade
/// epoch. Without the policy here, ONE compromised release key could sign an arbitrarily high epoch and
/// jam the store (permanently rejecting all future legitimate updates via INT008). A fresh install
/// (<c>requireSigned</c> false) passes no policy table: it never consults or advances the store, so the
/// C14 fresh-install posture is unchanged.</para>
/// </summary>
internal static class BundleTrustGate
{
    /// <summary>
    /// Verifies an already-deserialized manifest + overlay TOC (the bootstrapper path). On the
    /// require-signed path the persisted <paramref name="trustState"/> supplies the anti-downgrade epoch
    /// (INT008) and local revocations (INT001); a fresh install (<paramref name="requireSigned"/> false)
    /// ignores the store entirely.
    /// </summary>
    internal static Result<Unit> Verify(
        InstallerManifest manifest,
        IReadOnlyList<TocEntry> tocEntries,
        bool requireSigned,
        TrustState trustState)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tocEntries);
        ArgumentNullException.ThrowIfNull(trustState);

        return SignedPayloadTocVerifier.Verify(
            manifest, tocEntries, EngineTrustAnchor.EffectiveFingerprints, requireSigned,
            storedEpoch: requireSigned ? trustState.Epoch : 0,
            revokedFingerprints: RevokedSet(requireSigned, trustState),
            policyTable: requireSigned ? EngineTrustAnchor.EffectivePolicyTable : null,
            roles: EngineTrustAnchor.EffectiveRoles,
            pqPolicy: EngineTrustAnchor.CreatePqPolicy(PqFallbackLog));
    }

    /// <summary>
    /// Verifies raw extracted bundle content (the <c>--extract</c> self-extraction path). Deserializes the
    /// embedded manifest via <see cref="BundleTrustVerifier"/> and applies the same decision as the
    /// manifest + TOC overload.
    /// </summary>
    internal static Result<Unit> Verify(BundleContent content, bool requireSigned, TrustState trustState)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(trustState);

        return BundleTrustVerifier.VerifyBundleContent(
            content, EngineTrustAnchor.EffectiveFingerprints, requireSigned,
            storedEpoch: requireSigned ? trustState.Epoch : 0,
            revokedFingerprints: RevokedSet(requireSigned, trustState),
            policyTable: requireSigned ? EngineTrustAnchor.EffectivePolicyTable : null,
            roles: EngineTrustAnchor.EffectiveRoles,
            pqPolicy: EngineTrustAnchor.CreatePqPolicy(PqFallbackLog));
    }

    // Loud log for the incapable-OS classical-fallback branch (PQ-hybrid Stage 1). Both gate call
    // sites are the bootstrap/self-extract paths, which report to stderr (Program does the same for
    // verification failures) — the degradation must be visible, never silent.
    private static void PqFallbackLog(string message) => Console.Error.WriteLine(message);

    private static IReadOnlySet<string>? RevokedSet(bool requireSigned, TrustState trustState) =>
        requireSigned && trustState.RevokedFingerprints.Length > 0
            ? new HashSet<string>(trustState.RevokedFingerprints, StringComparer.OrdinalIgnoreCase)
            : null;
}
