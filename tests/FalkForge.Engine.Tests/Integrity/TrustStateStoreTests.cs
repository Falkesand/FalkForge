namespace FalkForge.Engine.Tests.Integrity;

using FalkForge.Engine.Integrity;
using Xunit;

/// <summary>
/// The persisted anti-downgrade/revocation store (C14 Stage 2, §6.2). It records the highest key-epoch
/// this machine has accepted and the fingerprints explicitly revoked, and advances monotonically only —
/// never backwards — so a replayed older-epoch release cannot roll the client's trust state back.
/// </summary>
public sealed class TrustStateStoreTests
{
    private static string TempStorePath() =>
        Path.Combine(Path.GetTempPath(), $"falk-trust-{Guid.NewGuid():N}", "trust-state.json");

    [Fact]
    public void Load_MissingFile_ReturnsFirstRunState()
    {
        var state = TrustStateStore.Load(TempStorePath());

        Assert.Equal(0, state.Epoch);
        Assert.Empty(state.RevokedFingerprints);
    }

    [Fact]
    public void Advance_FromFirstRun_PersistsEpochAndRevocations()
    {
        var path = TempStorePath();
        try
        {
            var advance = TrustStateStore.Advance(path, epoch: 5, revoked: new[] { "AABB" });
            Assert.True(advance.IsSuccess, advance.IsFailure ? advance.Error.Message : null);

            var reloaded = TrustStateStore.Load(path);
            Assert.Equal(5, reloaded.Epoch);
            Assert.Contains("AABB", reloaded.RevokedFingerprints);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Advance_LowerEpoch_DoesNotRollBack_Monotonic()
    {
        var path = TempStorePath();
        try
        {
            TrustStateStore.Advance(path, epoch: 5, revoked: []);
            TrustStateStore.Advance(path, epoch: 3, revoked: []); // stale/replay — must not lower

            Assert.Equal(5, TrustStateStore.Load(path).Epoch);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Advance_MergesRevocations_Union()
    {
        var path = TempStorePath();
        try
        {
            TrustStateStore.Advance(path, epoch: 1, revoked: new[] { "AABB" });
            TrustStateStore.Advance(path, epoch: 2, revoked: new[] { "CCDD" });

            var state = TrustStateStore.Load(path);
            Assert.Contains("AABB", state.RevokedFingerprints);
            Assert.Contains("CCDD", state.RevokedFingerprints);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    private static void TryCleanup(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
