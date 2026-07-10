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
/// <para>Extracted from <c>Program.RunAsBootstrapper</c> / <c>Program.VerifySignedPayloadTrust</c> so both
/// call sites make the identical decision and the decision itself is unit-testable. The staged-update path
/// (<see cref="StagedUpdateVerifier"/>) applies the same verification over a downloaded artifact before it
/// is ever relaunched.</para>
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
            revokedFingerprints: RevokedSet(requireSigned, trustState));
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
            revokedFingerprints: RevokedSet(requireSigned, trustState));
    }

    private static IReadOnlySet<string>? RevokedSet(bool requireSigned, TrustState trustState) =>
        requireSigned && trustState.RevokedFingerprints.Length > 0
            ? new HashSet<string>(trustState.RevokedFingerprints, StringComparer.OrdinalIgnoreCase)
            : null;
}
