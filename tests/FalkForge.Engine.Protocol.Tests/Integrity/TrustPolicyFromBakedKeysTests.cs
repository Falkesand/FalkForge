namespace FalkForge.Engine.Protocol.Tests.Integrity;

using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

/// <summary>
/// <see cref="TrustPolicy.FromBakedKeys"/> builds a policy directly from a baked trusted-key set
/// (fingerprints, per-fingerprint roles, PQ companions), applying the same role-defaulting rule
/// <c>EngineTrustAnchor.Freeze</c> applies to its own baked set: a fingerprint present in the set
/// defaults to <see cref="TrustRole.Release"/> whether its roles-dict entry is <see cref="TrustRole.None"/>
/// or the entry is missing outright. Getting the "missing entirely" case wrong drops the fingerprint from
/// the effective roles map, which sends <c>PayloadIntegrityGate</c> to the verify-any path
/// (<c>Roles.Count == 0</c>) and defeats quorum enforcement.
/// </summary>
public sealed class TrustPolicyFromBakedKeysTests
{
    private const string FingerprintA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string FingerprintB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void FingerprintWithNoneRole_DefaultsToRelease_AndEnforcesQuorum()
    {
        var fingerprints = new HashSet<string>(new[] { FingerprintA }, StringComparer.OrdinalIgnoreCase);
        var roles = new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase)
        {
            [FingerprintA] = TrustRole.None,
        };

        var policy = TrustPolicy.FromBakedKeys(
            fingerprints, roles, bakedPqCompanions: null,
            requireSigned: false, isUpdatePath: false, storedEpoch: 0);

        // Roles configured -> the quorum path, not the C14 verify-any path (which only triggers on
        // Roles.Count == 0).
        Assert.True(policy.Roles.Count > 0);
        Assert.Equal(TrustRole.Release, policy.Roles[FingerprintA]);
    }

    [Fact]
    public void FingerprintAbsentFromRolesDict_DefaultsToRelease_AndEnforcesQuorum()
    {
        // The trap: a defaulting rule that iterates only the roles dict (instead of the fingerprint
        // SET) silently drops a fingerprint that has no roles-dict entry at all.
        var fingerprints = new HashSet<string>(new[] { FingerprintA }, StringComparer.OrdinalIgnoreCase);
        var roles = new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase);

        var policy = TrustPolicy.FromBakedKeys(
            fingerprints, roles, bakedPqCompanions: null,
            requireSigned: false, isUpdatePath: false, storedEpoch: 0);

        Assert.True(policy.Roles.Count > 0);
        Assert.Equal(TrustRole.Release, policy.Roles[FingerprintA]);
    }

    [Fact]
    public void PqCompanions_SurviveIntoPolicy()
    {
        var fingerprints = new HashSet<string>(new[] { FingerprintA }, StringComparer.OrdinalIgnoreCase);
        var roles = new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase);
        var companions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FingerprintA] = FingerprintB,
        };

        var policy = TrustPolicy.FromBakedKeys(
            fingerprints, roles, companions,
            requireSigned: false, isUpdatePath: false, storedEpoch: 0);

        Assert.NotNull(policy.PqCompanions);
        Assert.Equal(FingerprintB, policy.PqCompanions[FingerprintA]);
    }
}
