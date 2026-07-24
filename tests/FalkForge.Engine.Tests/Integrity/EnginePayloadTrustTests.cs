namespace FalkForge.Engine.Tests.Integrity;

using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

/// <summary>
/// Characterization spec for <see cref="EnginePayloadTrust"/> — pins the behavior of
/// <c>LoadTrustState</c> and <c>VerifySignedPayloadTrust</c> exactly as they behaved as private
/// methods on <c>Program</c> before this extraction.
/// </summary>
public sealed class EnginePayloadTrustTests
{
    [Fact]
    public void LoadTrustState_RequireSignedFalse_ReturnsNeutralSuccess_WithoutConsultingTheStore()
    {
        // Intent: a fresh install / inspection extract (requireSigned=false) never consults the
        // persisted per-machine trust store — it always gets a neutral TrustState (epoch 0, no
        // revocations), regardless of what is on disk at TrustStateStore.DefaultPath.
        var result = EnginePayloadTrust.LoadTrustState(requireSigned: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Epoch);
        Assert.Empty(result.Value.RevokedFingerprints);
    }

    [Fact]
    public void LoadTrustState_RequireSignedTrue_DelegatesToTrustStateStoreLoadValidated()
    {
        // Intent: on the require-signed (update launcher) path, LoadTrustState must delegate to
        // TrustStateStore.LoadValidated(TrustStateStore.DefaultPath) — the ACL-validated enforcement
        // read — not the tolerant Load(). This test pins the real behavior against the real
        // DefaultPath: on a dev/CI machine with no store provisioned (verified: neither
        // %ProgramData%\FalkForge\Trust nor trust-state.json exist), LoadValidated's own documented
        // behavior for a missing store is a first-run success with a neutral TrustState — the SAME
        // observable result as calling TrustStateStore.LoadValidated directly, which is exactly
        // what this assertion cross-checks (rather than guessing at LoadTrustState's own logic).
        var direct = TrustStateStore.LoadValidated(TrustStateStore.DefaultPath);
        var viaHelper = EnginePayloadTrust.LoadTrustState(requireSigned: true);

        Assert.Equal(direct.IsSuccess, viaHelper.IsSuccess);
        if (direct.IsSuccess)
        {
            Assert.Equal(direct.Value.Epoch, viaHelper.Value.Epoch);
            Assert.Equal(direct.Value.RevokedFingerprints, viaHelper.Value.RevokedFingerprints);
        }
        else
        {
            Assert.Equal(direct.Error.Kind, viaHelper.Error.Kind);
        }
    }

    private static BundleContent UnsignedContent() => new()
    {
        BundlePath = "unused.exe",
        ManifestJsonBytes = null,
        TocEntries =
        [
            new TocEntry
            {
                PackageId = "MyPackage",
                Offset = 0,
                CompressedSize = 16,
                OriginalSize = 32,
                Sha256Hash = "deadbeef"
            }
        ]
    };

    [Fact]
    public void VerifySignedPayloadTrust_UnsignedBundle_RequireSignedFalse_PassesThrough()
    {
        // Intent: an unsigned/legacy bundle a user chose to run must still extract on a fresh
        // install — backward compatibility (C14).
        var result = EnginePayloadTrust.VerifySignedPayloadTrust(UnsignedContent(), requireSigned: false);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void VerifySignedPayloadTrust_UnsignedBundle_RequireSignedTrue_FailsWithInt007()
    {
        // Intent: on the require-signed update path (asserted by the update launcher), a
        // stripped/unsigned bundle is rejected before any payload is extracted (C14 Stage 2 / B2).
        var result = EnginePayloadTrust.VerifySignedPayloadTrust(UnsignedContent(), requireSigned: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT007", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifySignedPayloadTrust_DefaultParameter_RequireSignedFalse()
    {
        // Intent: the default-parameter overload used by callers that omit requireSigned must
        // behave identically to explicitly passing false.
        var result = EnginePayloadTrust.VerifySignedPayloadTrust(UnsignedContent());

        Assert.True(result.IsSuccess);
    }
}
