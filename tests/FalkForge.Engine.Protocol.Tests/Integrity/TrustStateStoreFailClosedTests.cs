using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// Corrupt-vs-missing distinction on the enforcement read path (owner decision): a MISSING store
/// file is a legitimate first run (epoch 0, no revocations), but a store file that EXISTS and fails
/// to parse must FAIL CLOSED — treating corruption as a first run would silently reset the
/// anti-downgrade epoch and revocation floor to zero, handing an attacker who can corrupt (but not
/// coherently rewrite) the file exactly the rollback the store exists to prevent.
///
/// <para>These tests target the internal fail-closed load core directly because on Windows the
/// public <see cref="TrustStateStore.LoadValidated"/> ACL anti-squat gate fires first for any
/// non-hardened test directory and would mask the corrupt-file decision; the end-to-end wiring
/// through <see cref="TrustStateStore.LoadValidated"/> behind a conforming ACL is covered by
/// TrustStateStoreTests.LoadValidated_MalformedJson_FailsClosed_WindowsElevatedOnly
/// (elevation-gated).</para>
/// </summary>
public sealed class TrustStateStoreFailClosedTests
{
    private static string TempStorePath() =>
        Path.Combine(Path.GetTempPath(), $"falk-trust-fc-{Guid.NewGuid():N}", "trust-state.json");

    [Fact]
    public void LoadFailClosed_MissingFile_ReturnsFirstRunState()
    {
        // A missing file is the legitimate first run — it must stay indistinguishable from a fresh
        // machine (epoch 0, no revocations) and must NOT fail closed, or no machine could ever
        // install its first update.
        var result = TrustStateStore.LoadFailClosed(TempStorePath());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(0, result.Value.Epoch);
        Assert.Empty(result.Value.RevokedFingerprints);
    }

    [Fact]
    public void LoadFailClosed_MalformedJson_FailsClosed_NotSilentEpochZeroReset()
    {
        var path = TempStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "{ this is not valid json ]");

            var result = TrustStateStore.LoadFailClosed(path);

            Assert.True(result.IsFailure,
                "a malformed store must fail closed — never silently reset to a first-run epoch 0");
            Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
            Assert.Contains("malformed", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Load_MalformedJson_AdvisoryPath_StaysTolerant_FirstRun()
    {
        // The plain (advisory) Load keeps degrading a malformed file to first-run: it backs
        // Advance's read-modify-write, which runs elevated immediately after a VERIFIED update
        // apply and rewrites the store — the self-healing write path. Only the enforcement read
        // (LoadValidated / LoadFailClosed) fails closed on corruption.
        var path = TempStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "{ this is not valid json ]");

            var state = TrustStateStore.Load(path);

            Assert.Equal(0, state.Epoch);
            Assert.Empty(state.RevokedFingerprints);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}
