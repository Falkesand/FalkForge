namespace FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// The role(s) a trusted publisher key holds (C19 §3). Trust is no longer monolithic: a key is tagged
/// with one or more roles, and the per-operation quorum policy (<see cref="PolicyRule"/>) requires
/// specific role combinations for high-risk operations, so no single compromised key can unilaterally
/// ship a key change, downgrade, or revocation.
///
/// <para>A key's roles are one <see cref="int"/> — allocation-free, AOT-safe, and role membership is a
/// bit test. The text form (MSBuild <c>Roles=</c> metadata, <c>EngineTrustAnchor</c> calls) uses the role
/// <b>names</b>, parsed to flags at build/bootstrap time. Roles are NEVER read from a bundle: at verify
/// time the engine resolves each accepted fingerprint to its roles via the pinned trusted set, never from
/// the signed envelope. Putting a role claim inside the bundle would reopen the self-describing-trust hole
/// C14 closed.</para>
///
/// <para><b>Backward compatibility (§7.1).</b> An un-roled trusted key defaults to <see cref="Release"/>,
/// so the ship-with-nothing behavior (every trusted key is a release key, install/update need one release
/// signature) is exactly C14's "one signature from any trusted key."</para>
///
/// <para>The enum is closed and versioned. Unknown role tokens encountered at parse time are ignored
/// (forward-compat: a newer publisher's role name an older engine does not know contributes
/// <see cref="None"/>, never crashes).</para>
/// </summary>
[Flags]
public enum TrustRole
{
    /// <summary>No role. An unknown role token parses to this and contributes nothing to a quorum.</summary>
    None = 0,

    /// <summary>
    /// Release manager / release HSM — the everyday signing identity. Authorizes ordinary install and
    /// update signing. The default role for an un-roled trusted key, which preserves C14 flat behavior.
    /// </summary>
    Release = 1 << 0,

    /// <summary>
    /// Offline / cold-storage key in separate custody from <see cref="Release"/>. Co-signs a key change
    /// (rotation) so a compromised online release key cannot re-anchor trust alone.
    /// </summary>
    Recovery = 1 << 1,

    /// <summary>
    /// Security-team key in separate custody. Co-signs a downgrade and authors/co-signs a revocation —
    /// governance actions owned by incident response, not the release pipeline.
    /// </summary>
    Security = 1 << 2,

    /// <summary>
    /// Break-glass key: offline, tightly held, single purpose. Alone-sufficient to author a revocation
    /// (revoke-only) so a leaked break-glass key cannot itself ship code.
    /// </summary>
    EmergencyRevoke = 1 << 3,

    /// <summary>
    /// Automated build-pipeline key. Signs nightly / pre-release / channel-restricted builds. Explicitly
    /// not <see cref="Release"/>, so a compromised CI runner cannot ship to the stable channel.
    /// </summary>
    Ci = 1 << 4,

    /// <summary>
    /// An individual developer key (many). Local/dev-channel bundles only; never satisfies a production
    /// operation. A developer-only bundle fails every production policy rule.
    /// </summary>
    Developer = 1 << 5,

    /// <summary>
    /// Timestamp-authority key. Attests when a signature was made (feeds expiry, a later stage). Never
    /// counts toward an operation quorum — it is evidence of time, not of authorship.
    /// </summary>
    Timestamp = 1 << 6,
}
