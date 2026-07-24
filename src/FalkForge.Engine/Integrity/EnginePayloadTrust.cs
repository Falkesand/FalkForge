namespace FalkForge.Engine.Integrity;

using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// Payload-trust helpers shared by the engine's self-extract and bootstrapper entry points.
/// Extracted from <c>Program</c> (pure move) so the two run paths' trust-loading and
/// signed-payload-binding decisions are unit-testable in isolation from process bootstrap.
/// </summary>
internal static class EnginePayloadTrust
{
    /// <summary>
    /// Loads the per-machine trust state for a payload-trust gate. On the require-signed path (update
    /// launcher) the persisted store is loaded with ACL validation (C16) so the gate enforces the same
    /// anti-downgrade epoch (INT008) + local revocations (INT001) as the bootstrapper; a non-conforming
    /// (attacker-writable) store fails closed. Fresh / inspection extracts (requireSigned=false) do not
    /// consult the store — it is advanced only during a verified update apply — so a neutral
    /// <see cref="TrustState"/> is returned. Callers surface the failure in their own idiom.
    /// </summary>
    internal static Result<TrustState> LoadTrustState(bool requireSigned) =>
        requireSigned
            ? TrustStateStore.LoadValidated(TrustStateStore.DefaultPath)
            : Result<TrustState>.Success(new TrustState());

    /// <summary>
    /// Binds the payloads about to be extracted to the ECDSA-signed manifest hash (see
    /// <see cref="Protocol.Integrity.SignedPayloadTocVerifier"/>). Deserializes the embedded manifest from the
    /// bundle content; a bundle with no embedded manifest (unsigned/old) or an unsigned manifest passes
    /// through. A signed manifest whose overlay TOC hash disagrees with the signed hash is rejected.
    /// </summary>
    internal static Result<Unit> VerifySignedPayloadTrust(BundleContent content, bool requireSigned = false)
    {
        // Delegate to the shared verifier (Engine.Protocol.Integrity) so the engine self-extract path,
        // `forge extract`, and `forge migrate` all bind byte→TOC→signed identically. The engine pins its
        // baked trusted set (an attacker's re-signed bundle is rejected); on a fresh install
        // (requireSigned=false) a legacy/unsigned bundle the user chose to run still extracts, while on the
        // update path (requireSigned=true, asserted by the launcher) a stripped/unsigned update is rejected
        // (INT007) before any payload is extracted (C14 Stage 2 / B2).
        //
        // On the require-signed path, consult the persisted per-machine trust store so this
        // `--extract --require-signed` gate enforces the SAME anti-downgrade epoch (INT008) + local
        // revocations (INT001) as the bootstrapper path (§6.3), C14 Stage 3 fold-in. Fresh / inspection
        // extracts (requireSigned=false) do not consult the store — it is advanced only during a verified
        // update apply.
        // Anti-squat (C16): on the require-signed path, validate the store directory's ACL before trusting
        // its epoch/revocations; a non-conforming (attacker-writable) store fails closed rather than
        // silently weakening the anti-downgrade/revocation gate.
        var loaded = LoadTrustState(requireSigned);
        if (loaded.IsFailure)
            return Result<Unit>.Failure(loaded.Error);

        return BundleTrustGate.Verify(content, requireSigned, loaded.Value);
    }
}
