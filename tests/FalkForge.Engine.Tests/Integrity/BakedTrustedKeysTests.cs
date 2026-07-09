namespace FalkForge.Engine.Tests.Integrity;

using FalkForge.Engine.Integrity;
using Xunit;

/// <summary>
/// Proves the build-time trusted-key injection produced a valid, reachable constant and that the
/// no-publisher default is safe. The MSBuild target (TrustedKeys.targets) generates
/// <see cref="BakedTrustedKeys"/> from <c>FalkForgeTrustedKey</c> items; with none supplied it emits an
/// empty <see cref="System.Collections.Frozen.FrozenSet{T}"/>. An empty set means the engine pins no
/// publisher and verification falls back to consistency-only — it never fails open on the update path
/// (Stage 2 require-signed handles that). A build that DOES supply a key is verified out-of-band
/// (dotnet build -p:FalkForgeTrustedKey=... then inspecting the generated TrustedKeys.g.cs) because a
/// rebuild-in-test would be slow and lock-prone.
/// </summary>
public sealed class BakedTrustedKeysTests
{
    [Fact]
    public void Fingerprints_DefaultBuild_IsAnEmptyButNonNullSet()
    {
        Assert.NotNull(BakedTrustedKeys.Fingerprints);
        Assert.Empty(BakedTrustedKeys.Fingerprints);
    }

    [Fact]
    public void TrustPolicy_FreshInstall_FromBakedSet_DoesNotRequireSignature()
    {
        var policy = TrustPolicy.FreshInstall(BakedTrustedKeys.Fingerprints);

        Assert.False(policy.RequireSigned);
        Assert.Same(BakedTrustedKeys.Fingerprints, policy.TrustedFingerprints);
    }

    [Fact]
    public void TrustPolicy_ConsistencyOnly_IsEmptyAndNotRequired()
    {
        Assert.Empty(TrustPolicy.ConsistencyOnly.TrustedFingerprints);
        Assert.False(TrustPolicy.ConsistencyOnly.RequireSigned);
    }
}
