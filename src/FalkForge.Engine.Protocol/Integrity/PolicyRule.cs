namespace FalkForge.Engine.Protocol.Integrity;

using System.Runtime.InteropServices;

/// <summary>
/// The operation a verify is establishing trust for (C19 §5.1). Resolved at verify time from signals that
/// are already signed (the install-vs-update path and the envelope epoch relative to the stored epoch), not
/// read from the bundle — an attacker cannot relabel a key change as an install to dodge the stricter rule
/// without breaking the signature (the epoch is signed) or landing on the fresh-install path (which they
/// only reach if the user runs their artifact, outside the model).
/// </summary>
public enum OperationKind
{
    /// <summary>Fresh install (bootstrapper / self-extract). Not a require-signed path.</summary>
    Install,

    /// <summary>Routine update, same key-epoch as the stored one.</summary>
    Update,

    /// <summary>Rotation: the signed epoch is above the stored epoch, re-anchoring trust.</summary>
    KeyChange,

    /// <summary>An explicitly-permitted lower version (operator-initiated; distinct from a blocked replay).</summary>
    Downgrade,

    /// <summary>The envelope declares revoked fingerprints.</summary>
    Revoke,
}

/// <summary>
/// A single AND-requirement in a <see cref="PolicyRule"/> (§5.1): at least <see cref="Count"/> DISTINCT
/// trusted keys, each holding any bit of <see cref="Role"/>. <see cref="Role"/> may be a flag union
/// (e.g. <c>Security | EmergencyRevoke</c>) to express a role-OR within one requirement — a key satisfies
/// it if it holds any named bit.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct RoleRequirement(TrustRole Role, int Count);

/// <summary>
/// One operation's quorum rule (§5.1): every <see cref="RoleRequirement"/> must be satisfiable
/// simultaneously by DISTINCT keys (no key counts for more than one requirement), and the total distinct
/// signatures must be at least <see cref="MinDistinctSignatures"/>. <c>[(Release,1)]</c> is "one release
/// key"; <c>[(Release,1),(Recovery,1)]</c> is "one release AND one recovery, held by different keys"; a
/// bare M-of-N with no role constraint is expressed via <see cref="MinDistinctSignatures"/>.
/// </summary>
public sealed record PolicyRule(IReadOnlyList<RoleRequirement> Requirements, int MinDistinctSignatures);
