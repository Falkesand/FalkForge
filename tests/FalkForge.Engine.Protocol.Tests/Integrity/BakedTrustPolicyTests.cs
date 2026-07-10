using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// The baked default per-operation policy table (§5.2) and the verify-time operation resolution (§5.3).
/// The table maps each operation to a required-roles + minimum-distinct-signature rule; it is a code
/// constant covered by the engine's own integrity (the channel the attacker cannot rewrite). Operation
/// resolution is derived from signals already signed (the path and the epoch relative to the stored epoch),
/// so an attacker cannot relabel a key-change as an install to dodge the stricter rule.
/// </summary>
public sealed class BakedTrustPolicyTests
{
    // ── Operation resolution (§5.3) ──────────────────────────────────────────

    [Fact]
    public void FreshInstallPath_ResolvesInstall()
    {
        Assert.Equal(OperationKind.Install,
            BakedTrustPolicy.ResolveOperation(isUpdatePath: false, envelopeEpoch: 0, storedEpoch: 0));
    }

    [Fact]
    public void UpdatePath_EqualEpoch_ResolvesUpdate()
    {
        Assert.Equal(OperationKind.Update,
            BakedTrustPolicy.ResolveOperation(isUpdatePath: true, envelopeEpoch: 5, storedEpoch: 5));
    }

    [Fact]
    public void UpdatePath_HigherEpoch_ResolvesKeyChange()
    {
        // A signed epoch above the stored epoch is a rotation (re-anchoring trust), the highest-risk
        // everyday operation — it must resolve to KeyChange and apply the stricter release+recovery rule.
        Assert.Equal(OperationKind.KeyChange,
            BakedTrustPolicy.ResolveOperation(isUpdatePath: true, envelopeEpoch: 6, storedEpoch: 5));
    }

    // ── Default table (§5.2) ─────────────────────────────────────────────────

    [Fact]
    public void Install_And_Update_RequireOneRelease()
    {
        AssertRule(BakedTrustPolicy.RuleFor(OperationKind.Install, hasRevocations: false), 1, (TrustRole.Release, 1));
        AssertRule(BakedTrustPolicy.RuleFor(OperationKind.Update, hasRevocations: false), 1, (TrustRole.Release, 1));
    }

    [Fact]
    public void KeyChange_RequiresReleaseAndRecovery_TwoDistinct()
    {
        AssertRule(BakedTrustPolicy.RuleFor(OperationKind.KeyChange, hasRevocations: false),
            2, (TrustRole.Release, 1), (TrustRole.Recovery, 1));
    }

    [Fact]
    public void Downgrade_RequiresReleaseAndSecurity_TwoDistinct()
    {
        AssertRule(BakedTrustPolicy.RuleFor(OperationKind.Downgrade, hasRevocations: false),
            2, (TrustRole.Release, 1), (TrustRole.Security, 1));
    }

    [Fact]
    public void Revoke_RequiresRelease_And_SecurityOrEmergencyRevoke()
    {
        AssertRule(BakedTrustPolicy.RuleFor(OperationKind.Revoke, hasRevocations: true),
            2, (TrustRole.Release, 1), (TrustRole.Security | TrustRole.EmergencyRevoke, 1));
    }

    [Fact]
    public void RevocationOverlay_MergesRevokeRequirement_IntoBaseOperation()
    {
        // A bundle that declares revocations (hasRevocations) must additionally satisfy the Revoke rule
        // on top of the base operation — so a lone release key cannot author a revocation on an update.
        var rule = BakedTrustPolicy.RuleFor(OperationKind.Update, hasRevocations: true);

        Assert.Contains(rule.Requirements, r => (r.Role & TrustRole.Security) != TrustRole.None || (r.Role & TrustRole.EmergencyRevoke) != TrustRole.None);
        Assert.True(rule.MinDistinctSignatures >= 2);
    }

    private static void AssertRule(PolicyRule rule, int minDistinct, params (TrustRole role, int count)[] reqs)
    {
        Assert.Equal(minDistinct, rule.MinDistinctSignatures);
        foreach (var (role, count) in reqs)
            Assert.Contains(rule.Requirements, r => r.Role == role && r.Count == count);
    }
}
