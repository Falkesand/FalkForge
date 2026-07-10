namespace FalkForge.Engine.Protocol.Integrity;

using System.Collections.Frozen;

/// <summary>
/// The baked default per-operation trust policy (C19 §5.2, §4.1). It maps each <see cref="OperationKind"/>
/// to a <see cref="PolicyRule"/> and resolves the operation from verify-time signals. It is a code constant
/// compiled into the engine, so it is covered by the engine binary's own integrity — the channel §2.2
/// assumes the attacker cannot rewrite. It cannot be stripped (there is nothing in the bundle to strip) or
/// rolled back (it is code). A bundle-carried, tighten-only overlay is a later stage; Stage 1 ships only
/// this baked floor.
///
/// <para><b>Backward compatibility (§7.1).</b> Under the default table, install and update require one
/// release signature. Because an un-roled trusted key defaults to <see cref="TrustRole.Release"/>, that is
/// exactly C14's "one signature from any trusted key." The stricter rules (key change, downgrade, revoke)
/// only bite once a publisher tags keys with distinct roles; an un-migrated engine (no roles configured)
/// keeps its C14 verify-any behavior because the gates take the trivial path when no roles are present.</para>
/// </summary>
public static class BakedTrustPolicy
{
    private static readonly PolicyRule InstallRule = new([new RoleRequirement(TrustRole.Release, 1)], 1);
    private static readonly PolicyRule UpdateRule = new([new RoleRequirement(TrustRole.Release, 1)], 1);

    private static readonly PolicyRule KeyChangeRule = new(
        [new RoleRequirement(TrustRole.Release, 1), new RoleRequirement(TrustRole.Recovery, 1)], 2);

    private static readonly PolicyRule DowngradeRule = new(
        [new RoleRequirement(TrustRole.Release, 1), new RoleRequirement(TrustRole.Security, 1)], 2);

    // Revoke: release AND (security OR emergency-revoke), expressed as a flag-union role within one
    // requirement so the model stays a flat AND-of-requirements while allowing role-OR inside a requirement.
    private static readonly PolicyRule RevokeRule = new(
        [
            new RoleRequirement(TrustRole.Release, 1),
            new RoleRequirement(TrustRole.Security | TrustRole.EmergencyRevoke, 1)
        ],
        2);

    /// <summary>The default per-operation policy table (§5.2). A code constant baked into the engine.</summary>
    public static readonly FrozenDictionary<OperationKind, PolicyRule> Default =
        new Dictionary<OperationKind, PolicyRule>
        {
            [OperationKind.Install] = InstallRule,
            [OperationKind.Update] = UpdateRule,
            [OperationKind.KeyChange] = KeyChangeRule,
            [OperationKind.Downgrade] = DowngradeRule,
            [OperationKind.Revoke] = RevokeRule,
        }.ToFrozenDictionary();

    /// <summary>
    /// Resolves the operation from the verify context (§5.3): the fresh-install vs update path, and the
    /// signed epoch relative to the stored epoch. Within the update family, a signed epoch above the stored
    /// epoch is a rotation (<see cref="OperationKind.KeyChange"/>); an equal epoch is a routine
    /// <see cref="OperationKind.Update"/>. A below-stored epoch is rejected as a replay (INT008) before this
    /// runs, and an intentional downgrade is an explicitly-requested path, never inferred from a low epoch.
    /// </summary>
    public static OperationKind ResolveOperation(bool isUpdatePath, int envelopeEpoch, int storedEpoch)
    {
        if (!isUpdatePath)
            return OperationKind.Install;

        return envelopeEpoch > storedEpoch ? OperationKind.KeyChange : OperationKind.Update;
    }

    /// <summary>
    /// The effective rule for <paramref name="operation"/>, overlaying the Revoke requirement when
    /// <paramref name="hasRevocations"/> is true (§5.3 step 3): any bundle that declares revocations must
    /// additionally satisfy the Revoke rule, so a lone release key cannot author a revocation. The overlay
    /// is AND-merged with the base operation's rule.
    /// </summary>
    public static PolicyRule RuleFor(OperationKind operation, bool hasRevocations) =>
        RuleFrom(Default, operation, hasRevocations);

    /// <summary>
    /// The effective rule for <paramref name="operation"/> from an arbitrary policy <paramref name="table"/>
    /// (the baked default, or a test/publisher table), applying the Revoke overlay when
    /// <paramref name="hasRevocations"/> is true. Used by the gates, which resolve against the effective
    /// table threaded through <c>TrustPolicy</c>.
    /// </summary>
    public static PolicyRule RuleFrom(
        IReadOnlyDictionary<OperationKind, PolicyRule> table, OperationKind operation, bool hasRevocations)
    {
        ArgumentNullException.ThrowIfNull(table);

        var baseRule = table[operation];
        if (operation == OperationKind.Revoke)
            return baseRule;
        if (!hasRevocations)
            return baseRule;

        return Merge(baseRule, table[OperationKind.Revoke]);
    }

    // AND-merge two rules by combining requirements keyed on role mask (taking the max count per role),
    // with MinDistinctSignatures the greater of the two. The distinct-key matching then enforces the real
    // cross-requirement distinctness.
    private static PolicyRule Merge(PolicyRule a, PolicyRule b)
    {
        var byRole = new Dictionary<TrustRole, int>();
        foreach (var req in a.Requirements)
            byRole[req.Role] = byRole.TryGetValue(req.Role, out var cur) ? Math.Max(cur, req.Count) : req.Count;
        foreach (var req in b.Requirements)
            byRole[req.Role] = byRole.TryGetValue(req.Role, out var cur) ? Math.Max(cur, req.Count) : req.Count;

        var merged = new List<RoleRequirement>(byRole.Count);
        foreach (var (role, count) in byRole)
            merged.Add(new RoleRequirement(role, count));

        return new PolicyRule(merged, Math.Max(a.MinDistinctSignatures, b.MinDistinctSignatures));
    }
}
