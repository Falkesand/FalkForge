namespace FalkForge.Engine.Protocol.Integrity;

using System.Security.Cryptography;

/// <summary>
/// The post-quantum companion inputs threaded into the envelope verifier (PQ-hybrid Stage 1,
/// design §2.2). The "this signer is hybrid, expect an ML-DSA companion" fact lives ONLY here —
/// in the pinned trust record baked into or registered with the engine — NEVER in the bundle, so
/// an attacker cannot un-declare a companion by rewriting bundle content (the same shape as the
/// C14 answer to self-describing keys: the bundle proves, the binary decides).
/// </summary>
public sealed class PqCompanionPolicy
{
    /// <summary>
    /// The pinned companion map: classical fingerprint → required ML-DSA companion fingerprint
    /// (both SHA-256-of-SPKI, uppercase hex). A trusted classical key present here counts only when
    /// the envelope also carries a matching, verifying ML-DSA companion signature (INT011
    /// otherwise, on a capable OS). A classical key absent from this map is classical-only and
    /// verifies exactly as before. Never null.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Companions { get; init; }

    /// <summary>
    /// Whether the verifying machine can verify ML-DSA signatures. Defaults to the real platform
    /// capability (<see cref="MLDsa.IsSupported"/>); injectable so the incapable-OS branch is
    /// deterministically testable on a capable machine.
    ///
    /// <para><b>SECURITY NOTE — why classical fallback is sound (human decision, design §8.1).</b>
    /// When this returns false the verifier accepts a hybrid-pinned key on its classical ECDSA-P256
    /// signature alone (with a loud log via <see cref="OnClassicalFallback"/>). That is safe ONLY
    /// because <see cref="MLDsa.IsSupported"/> reflects the victim machine's real OS/crypto stack
    /// and cannot be influenced by bundle content, network input, or anything else an attacker
    /// controls — an attacker cannot cause this branch to be taken. On a capable OS the companion
    /// rule is strict.</para>
    /// </summary>
    public Func<bool> IsPqSupported { get; init; } = static () => MLDsa.IsSupported;

    /// <summary>
    /// The loud-log sink invoked when a hybrid-pinned key is accepted on its classical signature
    /// alone because the OS cannot verify ML-DSA (see <see cref="IsPqSupported"/>). Engine callers
    /// wire this to their logger so the degradation is visible, never silent. Null = no sink
    /// (the acceptance behavior is identical).
    /// </summary>
    public Action<string>? OnClassicalFallback { get; init; }
}
