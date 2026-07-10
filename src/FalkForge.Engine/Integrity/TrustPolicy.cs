namespace FalkForge.Engine.Integrity;

using System.Collections.Frozen;
using FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// The trust inputs threaded into the integrity gates: which publisher-key fingerprints the engine
/// pins, and whether a signature is mandatory on this path.
///
/// <para><b>Trusted set.</b> A non-empty set (the engine's baked-in fingerprints, see
/// <see cref="BakedTrustedKeys"/>) turns on authorship: a signature is accepted only when its key's
/// fingerprint is pinned, so an attacker who re-signs a rewritten bundle with their own key is
/// rejected. An empty set means the engine was built with no publisher pin — verification falls back
/// to consistency-only (tamper-evidence, not authorship), which is the pre-pin backward-compatible
/// behavior. An empty set is <b>not</b> fail-open on the update path: Stage 2's require-signed policy
/// rejects unsigned/untrusted updates regardless.</para>
///
/// <para><b>RequireSigned.</b> Stage 1 always constructs fresh-install policies with
/// <see cref="RequireSigned"/> = false (an unsigned bundle the user chose to run still installs). The
/// field is the seam the Stage 2 update path flips to true so a stripped/absent signature is rejected.</para>
/// </summary>
internal readonly struct TrustPolicy
{
    /// <summary>The pinned trusted-key fingerprints (uppercase hex, no separators). Never null.</summary>
    public IReadOnlySet<string> TrustedFingerprints { get; }

    /// <summary>When true, an unsigned manifest is rejected instead of passed through.</summary>
    public bool RequireSigned { get; }

    /// <summary>
    /// The role(s) each trusted fingerprint holds (C19 §3), keyed by uppercase-hex fingerprint. Empty
    /// means "no roles configured" — the gates then take the C14 verify-any path (bit-for-bit backward
    /// compatible, §7.1). Never null.
    /// </summary>
    public IReadOnlyDictionary<string, TrustRole> Roles { get; }

    /// <summary>
    /// The effective per-operation quorum rules (C19 §5). Null when no policy is configured — combined with
    /// empty <see cref="Roles"/>, this keeps the gates on the trivial C14 path. Non-null activates the
    /// collect-all-distinct + quorum evaluation once roles are also present.
    /// </summary>
    public IReadOnlyDictionary<OperationKind, PolicyRule>? Rules { get; }

    public TrustPolicy(IReadOnlySet<string> trustedFingerprints, bool requireSigned)
        : this(trustedFingerprints, requireSigned, EmptyRoles, rules: null)
    {
    }

    public TrustPolicy(
        IReadOnlySet<string> trustedFingerprints,
        bool requireSigned,
        IReadOnlyDictionary<string, TrustRole> roles,
        IReadOnlyDictionary<OperationKind, PolicyRule>? rules)
    {
        ArgumentNullException.ThrowIfNull(trustedFingerprints);
        ArgumentNullException.ThrowIfNull(roles);
        TrustedFingerprints = trustedFingerprints;
        RequireSigned = requireSigned;
        Roles = roles;
        Rules = rules;
    }

    private static readonly FrozenDictionary<string, TrustRole> EmptyRoles =
        FrozenDictionary<string, TrustRole>.Empty;

    /// <summary>
    /// The no-pin, no-requirement policy: consistency-only verification, unsigned bundles pass.
    /// Used when the engine carries no baked trusted keys.
    /// </summary>
    public static TrustPolicy ConsistencyOnly { get; } = new(FrozenSet<string>.Empty, requireSigned: false);

    /// <summary>
    /// A fresh-install policy pinned to <paramref name="trustedFingerprints"/>: signatures must match
    /// a pinned key, but an unsigned bundle still installs (the user chose to run this artifact). No roles
    /// or quorum rules — the gate takes the C14 verify-any path.
    /// </summary>
    public static TrustPolicy FreshInstall(IReadOnlySet<string> trustedFingerprints) =>
        new(trustedFingerprints, requireSigned: false);

    /// <summary>
    /// A fresh-install policy carrying roles and the per-operation quorum rules (C19). When
    /// <paramref name="roles"/> is empty the gate still behaves exactly as C14; when roles are present the
    /// Install rule is enforced via the quorum evaluator.
    /// </summary>
    public static TrustPolicy FreshInstall(
        IReadOnlySet<string> trustedFingerprints,
        IReadOnlyDictionary<string, TrustRole> roles,
        IReadOnlyDictionary<OperationKind, PolicyRule> rules) =>
        new(trustedFingerprints, requireSigned: false, roles, rules);
}
