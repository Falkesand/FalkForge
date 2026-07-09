namespace FalkForge.Engine.Integrity;

using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// The in-process trust gate the already-installed, already-trusted engine runs over a <b>downloaded</b>
/// update bundle BEFORE it relaunches it (C14 Stage 3, FIX 1).
///
/// <para><b>Why this exists.</b> A downloaded update is fetched from an attacker-controllable feed and its
/// SHA-256 comes from that same feed, so a SHA-256 check proves nothing about authorship. Relaunching the
/// downloaded EXE and passing it <c>--require-signed</c> is trust theater: the downloaded artifact carries
/// its <i>own</i> embedded engine, which is free to ignore the flag. The trust decision must therefore be
/// made HERE — by the engine the user already trusts — over the staged bytes, never delegated to the
/// artifact being constrained. This verifier extracts the staged bundle's manifest + TOC and binds
/// byte→TOC→signed against the engine's baked trusted set with <c>requireSigned</c> on, so a stripped
/// (INT007), untrusted-key-re-signed (INT001), or payload-tampered (INT006) update is rejected before a
/// single payload is extracted or the bundle is launched.</para>
/// </summary>
internal static class StagedUpdateVerifier
{
    /// <summary>
    /// Verifies a staged update bundle at <paramref name="stagedBundlePath"/> against an explicit
    /// <paramref name="trustedFingerprints"/> set, always require-signed. Extraction failure, an unsigned
    /// manifest, an untrusted/invalid signature, a downgrade/replay, a revoked key, or a post-signing TOC
    /// tamper all produce a failure Result — the caller must NOT launch on failure.
    /// </summary>
    internal static Result<Unit> Verify(
        string stagedBundlePath,
        IReadOnlySet<string> trustedFingerprints,
        int storedEpoch,
        IReadOnlySet<string>? revokedFingerprints)
    {
        ArgumentException.ThrowIfNullOrEmpty(stagedBundlePath);
        ArgumentNullException.ThrowIfNull(trustedFingerprints);

        var extract = BundleReader.Extract(stagedBundlePath);
        if (extract.IsFailure)
            return Result<Unit>.Failure(extract.Error);

        // requireSigned: true — an update is an automatic, unattended replacement of already-trusted
        // software, so a missing/stripped signature is itself a rejection (design §3.4).
        return BundleTrustVerifier.VerifyBundleContent(
            extract.Value, trustedFingerprints, requireSigned: true, storedEpoch, revokedFingerprints);
    }

    /// <summary>
    /// Production default: verify against the engine's effective trusted set (the baked set unioned with any
    /// keys registered from bootstrap code, <see cref="EngineTrustAnchor"/>), consulting the persisted
    /// per-machine trust store for the anti-downgrade epoch and locally-revoked fingerprints (§6.3).
    /// </summary>
    internal static Result<Unit> VerifyWithBakedTrust(string stagedBundlePath)
    {
        var state = TrustStateStore.Load(TrustStateStore.DefaultPath);
        IReadOnlySet<string>? revoked = state.RevokedFingerprints.Length > 0
            ? new HashSet<string>(state.RevokedFingerprints, StringComparer.OrdinalIgnoreCase)
            : null;

        return Verify(stagedBundlePath, EngineTrustAnchor.EffectiveFingerprints, state.Epoch, revoked);
    }
}
