namespace FalkForge.Engine.Protocol.Integrity;

using System.Text;

/// <summary>
/// Evaluates a set of collected, distinct, trusted signatures against an operation's
/// <see cref="PolicyRule"/> (C19 §5.4). The rule is satisfied iff (1) the count of distinct signatures is
/// at least <see cref="PolicyRule.MinDistinctSignatures"/>, and (2) there is an assignment of DISTINCT keys
/// to the requirements such that each <see cref="RoleRequirement"/> is met by <see cref="RoleRequirement.Count"/>
/// distinct keys each holding any bit of its role, with no key used for more than one requirement.
///
/// <para><b>Distinct-key enforcement is the crux.</b> A single key holding <c>release | recovery</c> must
/// NOT satisfy both <c>(Release,1)</c> and <c>(Recovery,1)</c> alone — that would defeat quorum. Condition
/// (2) is a bipartite matching (keys to requirement-slots) that forbids reusing one key across two slots,
/// so a two-role rule genuinely needs two different private keys.</para>
///
/// <para>Cardinalities here are tiny (a handful of requirements, small counts), so a Kuhn augmenting-path
/// matching over arrays is correct and allocation-light — no LINQ in the decision path (Gate 6). Callers
/// must pass a set already deduplicated by fingerprint (as <see cref="IntegrityEnvelopeCodec.CollectTrustedSignatures"/>
/// guarantees), so each member is a distinct key.</para>
/// </summary>
public static class QuorumEvaluator
{
    /// <summary>The outcome of a quorum evaluation: whether the rule is satisfied and a human diagnostic.</summary>
    public readonly record struct QuorumDecision(bool Satisfied, string Diagnostic);

    /// <summary>
    /// Evaluates <paramref name="collected"/> (distinct trusted signatures, each resolved to its roles)
    /// against <paramref name="rule"/>. Returns whether the rule is satisfied plus a diagnostic naming the
    /// requirement and what was present, for a fail-loud INT010 message.
    /// </summary>
    public static QuorumDecision Evaluate(IReadOnlyList<TrustedSignature> collected, PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(collected);
        ArgumentNullException.ThrowIfNull(rule);

        if (collected.Count < rule.MinDistinctSignatures)
            return new QuorumDecision(false, Describe(collected, rule));

        // Expand each requirement into Count slots; every slot carries the requirement's role mask.
        var slots = new List<TrustRole>();
        foreach (var req in rule.Requirements)
        {
            var count = req.Count < 0 ? 0 : req.Count;
            for (var i = 0; i < count; i++)
                slots.Add(req.Role);
        }

        // A rule with no role requirements (bare M-of-N) is satisfied by the count check above.
        if (slots.Count == 0)
            return new QuorumDecision(true, Describe(collected, rule));

        // Kuhn bipartite matching: try to saturate every slot with a distinct key.
        var slotToSig = new int[slots.Count];
        Array.Fill(slotToSig, -1);
        var sigToSlot = new int[collected.Count];
        Array.Fill(sigToSlot, -1);

        for (var s = 0; s < slots.Count; s++)
        {
            var visited = new bool[collected.Count];
            if (!TryAssign(s, slots, collected, slotToSig, sigToSlot, visited))
                return new QuorumDecision(false, Describe(collected, rule));
        }

        return new QuorumDecision(true, Describe(collected, rule));
    }

    // Augmenting-path search: assign slot to some key holding its role, reassigning already-matched keys
    // along the way. Forbids reusing one key across two slots (distinct-key guarantee).
    private static bool TryAssign(
        int slot,
        List<TrustRole> slots,
        IReadOnlyList<TrustedSignature> sigs,
        int[] slotToSig,
        int[] sigToSlot,
        bool[] visited)
    {
        var role = slots[slot];
        for (var j = 0; j < sigs.Count; j++)
        {
            if (visited[j] || (sigs[j].Roles & role) == TrustRole.None)
                continue;

            visited[j] = true;
            var currentSlot = sigToSlot[j];
            if (currentSlot == -1 || TryAssign(currentSlot, slots, sigs, slotToSig, sigToSlot, visited))
            {
                slotToSig[slot] = j;
                sigToSlot[j] = slot;
                return true;
            }
        }

        return false;
    }

    private static string Describe(IReadOnlyList<TrustedSignature> collected, PolicyRule rule)
    {
        var sb = new StringBuilder();
        sb.Append("required: ");
        for (var i = 0; i < rule.Requirements.Count; i++)
        {
            if (i > 0)
                sb.Append(" AND ");
            var req = rule.Requirements[i];
            sb.Append(req.Count).Append('x').Append(req.Role);
        }
        if (rule.Requirements.Count == 0)
            sb.Append("(no role constraint)");
        sb.Append(" (min ").Append(rule.MinDistinctSignatures).Append(" distinct); present: ")
          .Append(collected.Count).Append(" distinct signature(s) with roles [");
        for (var i = 0; i < collected.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(collected[i].Roles);
        }
        sb.Append(']');
        return sb.ToString();
    }
}
