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

    /// <summary>
    /// True on the require-signed update path (C19 quorum uniformity): the gate resolves the operation
    /// from the signed epoch relative to <see cref="StoredEpoch"/> (same epoch → Update, epoch advance →
    /// KeyChange) instead of assuming Install, and enforces the anti-downgrade epoch (INT008). False for
    /// fresh installs, which never consult or advance the persisted trust store.
    /// </summary>
    public bool IsUpdatePath { get; }

    /// <summary>
    /// The highest key-epoch this machine has already accepted (from the persisted trust store).
    /// Consulted only when <see cref="IsUpdatePath"/> is true.
    /// </summary>
    public int StoredEpoch { get; }

    public TrustPolicy(IReadOnlySet<string> trustedFingerprints, bool requireSigned)
        : this(trustedFingerprints, requireSigned, EmptyRoles, rules: null)
    {
    }

    public TrustPolicy(
        IReadOnlySet<string> trustedFingerprints,
        bool requireSigned,
        IReadOnlyDictionary<string, TrustRole> roles,
        IReadOnlyDictionary<OperationKind, PolicyRule>? rules,
        bool isUpdatePath = false,
        int storedEpoch = 0)
    {
        ArgumentNullException.ThrowIfNull(trustedFingerprints);
        ArgumentNullException.ThrowIfNull(roles);
        TrustedFingerprints = trustedFingerprints;
        RequireSigned = requireSigned;
        Roles = roles;
        Rules = rules;
        IsUpdatePath = isUpdatePath;
        StoredEpoch = storedEpoch;
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

    /// <summary>
    /// The require-signed update-path policy (C19 quorum uniformity): a signature is mandatory, and the
    /// gate resolves the operation from the signed epoch relative to <paramref name="storedEpoch"/> (the
    /// persisted anti-downgrade epoch) exactly as the staged-update verifier does — a routine same-epoch
    /// update needs one release signature, an epoch advance is a key change requiring the
    /// release+recovery quorum. Used by the pipeline when the apply may advance the persisted trust store.
    /// </summary>
    public static TrustPolicy RequireSignedUpdate(
        IReadOnlySet<string> trustedFingerprints,
        IReadOnlyDictionary<string, TrustRole> roles,
        IReadOnlyDictionary<OperationKind, PolicyRule> rules,
        int storedEpoch) =>
        new(trustedFingerprints, requireSigned: true, roles, rules, isUpdatePath: true, storedEpoch);
}
