using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// The heart of C19: quorum evaluation replaces "first valid trusted signature wins" (1-of-N OR) with
/// "collect all distinct trusted signatures, resolve each to its key's roles, and evaluate against the
/// operation's policy rule." These tests encode WHY it exists — no single compromised key can satisfy a
/// two-role, two-distinct-key requirement (§5.4). The distinct-key rule is the crux: a key that happens to
/// hold both required roles must NOT single-handedly satisfy a two-role requirement, or quorum is defeated.
/// </summary>
public sealed class QuorumEvaluatorTests
{
    private static TrustedSignature Sig(string fp, TrustRole roles) => new(fp, roles);

    private static PolicyRule Rule(int minDistinct, params RoleRequirement[] reqs) => new(reqs, minDistinct);

    // ── M-of-N threshold ─────────────────────────────────────────────────────

    [Fact]
    public void TwoDistinctReleaseKeys_SatisfyThresholdOfTwo()
    {
        var collected = new[] { Sig("AA", TrustRole.Release), Sig("BB", TrustRole.Release) };

        var result = QuorumEvaluator.Evaluate(collected, Rule(2, new RoleRequirement(TrustRole.Release, 2)));

        Assert.True(result.Satisfied, result.Diagnostic);
    }

    [Fact]
    public void OneReleaseKey_FailsThresholdOfTwo()
    {
        var collected = new[] { Sig("AA", TrustRole.Release) };

        var result = QuorumEvaluator.Evaluate(collected, Rule(2, new RoleRequirement(TrustRole.Release, 2)));

        Assert.False(result.Satisfied);
    }

    // ── Role AND requirement ─────────────────────────────────────────────────

    [Fact]
    public void ReleasePlusRecovery_SatisfyKeyChangeRule()
    {
        var collected = new[] { Sig("AA", TrustRole.Release), Sig("BB", TrustRole.Recovery) };

        var result = QuorumEvaluator.Evaluate(
            collected, Rule(2, new RoleRequirement(TrustRole.Release, 1), new RoleRequirement(TrustRole.Recovery, 1)));

        Assert.True(result.Satisfied, result.Diagnostic);
    }

    [Fact]
    public void ReleaseOnly_FailsKeyChangeRule_MissingRecovery()
    {
        var collected = new[] { Sig("AA", TrustRole.Release) };

        var result = QuorumEvaluator.Evaluate(
            collected, Rule(2, new RoleRequirement(TrustRole.Release, 1), new RoleRequirement(TrustRole.Recovery, 1)));

        Assert.False(result.Satisfied);
    }

    // ── Distinct-key enforcement (the crux, §5.4) ────────────────────────────

    [Fact]
    public void SingleKeyHoldingBothRoles_DoesNotSatisfyTwoRoleRule_Alone()
    {
        // One key tagged release|recovery must NOT satisfy [(Release,1),(Recovery,1)] on its own — that
        // would defeat quorum. The distinct-key matching forbids reusing one key across two requirements.
        var collected = new[] { Sig("AA", TrustRole.Release | TrustRole.Recovery) };

        var result = QuorumEvaluator.Evaluate(
            collected, Rule(2, new RoleRequirement(TrustRole.Release, 1), new RoleRequirement(TrustRole.Recovery, 1)));

        Assert.False(result.Satisfied);
    }

    [Fact]
    public void TwoKeysEachHoldingBothRoles_SatisfyTwoRoleRule()
    {
        // With two distinct keys the requirement is satisfiable even if each holds both roles.
        var collected = new[]
        {
            Sig("AA", TrustRole.Release | TrustRole.Recovery),
            Sig("BB", TrustRole.Release | TrustRole.Recovery)
        };

        var result = QuorumEvaluator.Evaluate(
            collected, Rule(2, new RoleRequirement(TrustRole.Release, 1), new RoleRequirement(TrustRole.Recovery, 1)));

        Assert.True(result.Satisfied, result.Diagnostic);
    }

    // ── Augmenting-path matching (guards against a greedy-matcher regression) ─

    [Fact]
    public void DualRoleKeyOrderedFirst_AugmentingPathReassignsIt_LegitimateRotationAccepted()
    {
        // WHY THIS CASE EXISTS: every other case in this file also passes under a NAIVE GREEDY
        // matcher, so a refactor from Kuhn augmenting-path matching to greedy would slip through
        // green — and then FALSELY REJECT legitimate dual-role rotation bundles. Here a greedy
        // matcher walks the slots in rule order and grabs the FIRST key holding the role: slot
        // Release takes BB (release|recovery), leaving slot Recovery unfillable (AA is release-only)
        // → false rejection. Real matching augments: BB is reassigned to Recovery, AA fills Release.
        var collected = new[]
        {
            Sig("BB", TrustRole.Release | TrustRole.Recovery),
            Sig("AA", TrustRole.Release)
        };

        var result = QuorumEvaluator.Evaluate(
            collected, Rule(2, new RoleRequirement(TrustRole.Release, 1), new RoleRequirement(TrustRole.Recovery, 1)));

        Assert.True(result.Satisfied, result.Diagnostic);
    }

    [Fact]
    public void DualRoleKeyOrderedFirst_SymmetricOrdering_LegitimateRotationAccepted()
    {
        // Symmetric ordering of the case above (so neither slot order nor signature order can
        // accidentally rescue a greedy matcher): greedy fills slot Recovery with BB first, leaving
        // slot Release unfillable (AA is recovery-only); augmenting-path matching swaps them.
        var collected = new[]
        {
            Sig("BB", TrustRole.Release | TrustRole.Recovery),
            Sig("AA", TrustRole.Recovery)
        };

        var result = QuorumEvaluator.Evaluate(
            collected, Rule(2, new RoleRequirement(TrustRole.Recovery, 1), new RoleRequirement(TrustRole.Release, 1)));

        Assert.True(result.Satisfied, result.Diagnostic);
    }

    // ── Wrong role ───────────────────────────────────────────────────────────

    [Fact]
    public void DowngradeSignedByReleaseAndDeveloper_Rejected_NoSecurity()
    {
        // A downgrade requires release + security. release + developer has no security role → rejected.
        var collected = new[] { Sig("AA", TrustRole.Release), Sig("BB", TrustRole.Developer) };

        var result = QuorumEvaluator.Evaluate(
            collected, Rule(2, new RoleRequirement(TrustRole.Release, 1), new RoleRequirement(TrustRole.Security, 1)));

        Assert.False(result.Satisfied);
    }

    // ── Role-OR within one requirement (revoke rule) ─────────────────────────

    [Fact]
    public void RoleUnionRequirement_SatisfiedByEitherBit()
    {
        // The Revoke rule uses (Security | EmergencyRevoke): a key holding EITHER bit satisfies it.
        var collected = new[]
        {
            Sig("AA", TrustRole.Release),
            Sig("BB", TrustRole.EmergencyRevoke)
        };

        var result = QuorumEvaluator.Evaluate(
            collected,
            Rule(2,
                new RoleRequirement(TrustRole.Release, 1),
                new RoleRequirement(TrustRole.Security | TrustRole.EmergencyRevoke, 1)));

        Assert.True(result.Satisfied, result.Diagnostic);
    }

    [Fact]
    public void BelowMinDistinctSignatures_FailsEvenWhenRolesPresent()
    {
        // A single key holding every role cannot meet a min-distinct-signatures floor of 2.
        var collected = new[] { Sig("AA", TrustRole.Release | TrustRole.Security) };

        var result = QuorumEvaluator.Evaluate(collected, Rule(2, new RoleRequirement(TrustRole.Release, 1)));

        Assert.False(result.Satisfied);
    }

    [Fact]
    public void EmptyCollected_FailsAnyNonTrivialRule()
    {
        var result = QuorumEvaluator.Evaluate([], Rule(1, new RoleRequirement(TrustRole.Release, 1)));

        Assert.False(result.Satisfied);
    }

    // NOTE (negative-Count clamp, line ~44, and the slotToSig sentinel fill, line ~55): deliberately
    // NOT covered here. Both mutations were verified empirically (mutate production, run suite,
    // observe) to be equivalent given the current implementation, not merely untested:
    //   - `req.Count < 0 ? 0 : req.Count` clamps a negative Count to 0 before the loop
    //     `for (var i = 0; i < count; i++) slots.Add(...)`. That loop's own bound check already
    //     treats any negative count identically to 0 (the condition `i < count` is false at i=0 for
    //     ANY negative count), so removing the clamp produces byte-for-byte the same `slots` list.
    //     No PolicyRule / collected-signature input can make the clamped and unclamped code paths
    //     diverge — confirmed by running the full suite with the clamp deleted (`593/593` still pass).
    //   - `Array.Fill(slotToSig, -1)` seeds an array that TryAssign only ever WRITES to
    //     (`slotToSig[slot] = j` at the point a slot is matched); nothing in Evaluate or TryAssign
    //     ever reads slotToSig back — only sigToSlot's sentinel is read (line 86, `currentSlot == -1`)
    //     to detect a free signature. Confirmed by mutating the fill to `+1` and running the full
    //     suite plus three additional probes (an unmatched-slot-with-signature-index-1 case and two
    //     multi-slot reassignment-chain cases): all pass unchanged. slotToSig's initial value cannot
    //     be observed by any test against the current source.
    // A test asserting either mutation "fails" would be dishonest — it would pass whether or not the
    // production line is even present. If future work makes slotToSig's assignments part of the
    // returned QuorumDecision (e.g. to report which key filled which slot), that read would make the
    // sentinel's value observable again and this note should be revisited.
}
