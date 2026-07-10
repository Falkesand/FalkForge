namespace FalkForge.Engine.Tests.Integrity;

using System.Security.Cryptography;
using FalkForge.Engine.Integrity;
using Xunit;

/// <summary>
/// PQ-hybrid Stage 1 trust-anchor plumbing (design §2.3): a hybrid signer is ONE combined trust
/// record — classical fingerprint (the identity, carrying the roles) plus a pinned ML-DSA companion
/// fingerprint. The companion map is the ONLY place the "expect PQ" fact lives, it is registered
/// from compiled bootstrap code (or baked via TrustedKeys.targets), never read from a bundle, and a
/// companion fingerprint is never an independent trust anchor. Conflicting companion registrations
/// throw (fail loud — no silent last-wins on a security anchor), and a mixed set (companioned +
/// un-companioned keys) surfaces a configuration warning: PQ protection is only as strong as the
/// weakest pinned key.
/// </summary>
public sealed class PqHybridTrustAnchorTests : IDisposable
{
    public PqHybridTrustAnchorTests() => EngineTrustAnchor.ResetForTests();

    public void Dispose() => EngineTrustAnchor.ResetForTests();

    private static string RandomFingerprint()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }

    [Fact]
    public void TrustHybridFingerprint_PairIsPinned_CompanionIsNotAnIndependentAnchor()
    {
        var classicalFp = RandomFingerprint();
        var pqFp = RandomFingerprint();

        EngineTrustAnchor.TrustHybridFingerprint(classicalFp, pqFp);

        Assert.Contains(classicalFp, (IEnumerable<string>)EngineTrustAnchor.EffectiveFingerprints);
        // The PQ companion fingerprint must NOT enter the trusted set: it is a validity condition
        // on the classical identity, never a key that could satisfy classical-shaped rules or
        // quorum slots on its own.
        Assert.DoesNotContain(pqFp, (IEnumerable<string>)EngineTrustAnchor.EffectiveFingerprints);
        Assert.Equal(pqFp, EngineTrustAnchor.EffectivePqCompanions[classicalFp]);
    }

    [Fact]
    public void TrustHybridKey_DerivesBothFingerprintsExactlyAsTheVerifierDoes()
    {
        // The anchor must derive SHA-256-of-SPKI for BOTH halves with the same primitives the
        // envelope verifier uses, so a signed hybrid envelope matches a code-registered pair.
        var classicalSpki = new byte[91];
        var pqSpki = new byte[1974];
        RandomNumberGenerator.Fill(classicalSpki);
        RandomNumberGenerator.Fill(pqSpki);

        EngineTrustAnchor.TrustHybridKey(classicalSpki, pqSpki);

        var classicalFp = Convert.ToHexString(SHA256.HashData(classicalSpki));
        var pqFp = Convert.ToHexString(SHA256.HashData(pqSpki));
        Assert.Contains(classicalFp, (IEnumerable<string>)EngineTrustAnchor.EffectiveFingerprints);
        Assert.Equal(pqFp, EngineTrustAnchor.EffectivePqCompanions[classicalFp]);
    }

    [Fact]
    public void ConflictingCompanionRegistration_Throws_NoSilentLastWins()
    {
        var classicalFp = RandomFingerprint();
        EngineTrustAnchor.TrustHybridFingerprint(classicalFp, RandomFingerprint());

        // Registering a DIFFERENT companion for the same classical identity is a configuration
        // contradiction on a security anchor — fail loud, never last-wins.
        Assert.Throws<InvalidOperationException>(
            () => EngineTrustAnchor.TrustHybridFingerprint(classicalFp, RandomFingerprint()));
    }

    [Fact]
    public void SameCompanionRegisteredTwice_IsIdempotent()
    {
        var classicalFp = RandomFingerprint();
        var pqFp = RandomFingerprint();

        EngineTrustAnchor.TrustHybridFingerprint(classicalFp, pqFp);
        EngineTrustAnchor.TrustHybridFingerprint(classicalFp, pqFp); // same pair again — fine

        Assert.Equal(pqFp, EngineTrustAnchor.EffectivePqCompanions[classicalFp]);
    }

    [Fact]
    public void TrustHybridFingerprint_AfterFreeze_Throws()
    {
        _ = EngineTrustAnchor.EffectiveFingerprints; // freeze

        Assert.Throws<InvalidOperationException>(
            () => EngineTrustAnchor.TrustHybridFingerprint(RandomFingerprint(), RandomFingerprint()));
    }

    [Fact]
    public void MixedSet_CompanionedAndUncompanionedKeys_SurfacesConfigurationWarning()
    {
        // The weakest-link caveat (design §2.2): if hybrid key H and classical-only key L are both
        // pinned, a quantum forger simply targets L — PQ protection is only as strong as the
        // weakest pinned key. A mixed set is legitimate mid-migration, so it warns, never fails
        // (human decision §8.5).
        var hybridFp = RandomFingerprint();
        var classicalOnlyFp = RandomFingerprint();
        EngineTrustAnchor.TrustHybridFingerprint(hybridFp, RandomFingerprint());
        EngineTrustAnchor.TrustFingerprint(classicalOnlyFp);

        _ = EngineTrustAnchor.EffectiveFingerprints; // freeze, computing warnings

        Assert.Contains(EngineTrustAnchor.ConfigurationWarnings,
            w => w.Contains("post-quantum", StringComparison.OrdinalIgnoreCase)
                 && w.Contains(classicalOnlyFp, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AllKeysCompanioned_NoPqWarning()
    {
        EngineTrustAnchor.TrustHybridFingerprint(RandomFingerprint(), RandomFingerprint());
        EngineTrustAnchor.TrustHybridFingerprint(RandomFingerprint(), RandomFingerprint());

        _ = EngineTrustAnchor.EffectiveFingerprints;

        Assert.DoesNotContain(EngineTrustAnchor.ConfigurationWarnings,
            w => w.Contains("post-quantum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoCompanionsAtAll_NoPqWarning_PreHybridSetsStayQuiet()
    {
        // Every pre-PQ engine has zero companions — warning on that would be pure noise. The
        // warning is about the MIXED state only.
        EngineTrustAnchor.TrustFingerprint(RandomFingerprint());
        EngineTrustAnchor.TrustFingerprint(RandomFingerprint());

        _ = EngineTrustAnchor.EffectiveFingerprints;

        Assert.DoesNotContain(EngineTrustAnchor.ConfigurationWarnings,
            w => w.Contains("post-quantum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BakedPqCompanions_DefaultBuild_IsAnEmptyButNonNullMap()
    {
        // Proves TrustedKeys.targets generates the PqCompanions member (compile-time proof) and
        // that a build with no PqFingerprint metadata pins no companions. A build that DOES supply
        // PqFingerprint metadata is verified out-of-band (same idiom as BakedTrustedKeysTests).
        Assert.NotNull(BakedTrustedKeys.PqCompanions);
        Assert.Empty(BakedTrustedKeys.PqCompanions);
    }
}
