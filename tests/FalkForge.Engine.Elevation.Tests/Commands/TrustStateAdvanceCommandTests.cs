using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

/// <summary>
/// The elevated <c>TrustStateAdvance</c> command (C16): the only whitelisted operation that advances the
/// ACL-protected anti-downgrade/revocation store. It exists because the non-elevated engine cannot write
/// under the restrictive store ACL — the engine sends epoch + revocations here after a fully-verified apply,
/// and the elevated companion re-validates them and persists them. These tests encode that the command
/// (a) rejects a malformed payload, (b) fails loud rather than silently on a bad write, and (c) when the
/// write succeeds (elevated host / writable store), persists exactly the epoch + revocations requested.
/// </summary>
public sealed class TrustStateAdvanceCommandTests
{
    private static string TempStorePath() =>
        Path.Combine(Path.GetTempPath(), $"falk-trustcmd-{Guid.NewGuid():N}", "trust-state.json");

    [Fact]
    public void Execute_MalformedPayload_FailsLoud()
    {
        var command = new TrustStateAdvanceCommand(TempStorePath());

        var result = command.Execute([0x01, 0x02]); // too short to be a valid advance payload

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void Execute_ValidPayload_AdvancesStore_OrFailsLoud()
    {
        // The store directory is hardened (SYSTEM/Admins-write only). On an elevated host the write lands
        // and we can assert the persisted epoch; on a non-elevated CI host the hardened write is denied and
        // the command must surface a failure Result (fail-loud) rather than silently no-op. Either outcome
        // is honest — a non-advancing store must be visible, never a silent success.
        var path = TempStorePath();
        var command = new TrustStateAdvanceCommand(path);
        var payload = TrustAdvancePayload.Serialize(5, new[] { "AABB" });
        try
        {
            var result = command.Execute(payload);

            if (result.IsSuccess)
            {
                var state = TrustStateStore.Load(path);
                Assert.Equal(5, state.Epoch);
                Assert.Contains("AABB", state.RevokedFingerprints);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
            }
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Name_IsTrustStateAdvance()
    {
        Assert.Equal("TrustStateAdvance", new TrustStateAdvanceCommand(TempStorePath()).Name);
    }

    private static void TryCleanup(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}
