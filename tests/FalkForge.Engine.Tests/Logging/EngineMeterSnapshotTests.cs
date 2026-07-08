namespace FalkForge.Engine.Tests.Logging;

using FalkForge.Diagnostics;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Verifies that <see cref="EngineMeter.Snapshot"/> returns in-process counter values
/// and that <see cref="EngineMeter.FlushToLogger"/> writes a structured "Metrics" entry
/// through <see cref="IFalkLogger"/>.
///
/// WHY: Without an export path the counters are invisible outside the process. The
/// snapshot + logger bridge lets operators read session-level metrics from the log file
/// without requiring an OTel collector to be present.
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class EngineMeterSnapshotTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Test 1 — Snapshot reflects increments made via the recording API
    // WHY: The snapshot must accurately reflect how many times each recording
    //      method was called during the session so logged values are trustworthy.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Snapshot_ReflectsRecordedIncrements()
    {
        // Reset first so prior test runs in the same process don't bleed in.
        EngineMeter.ResetForTesting();

        EngineMeter.RecordPhaseTransition(EnginePhase.Detecting, elapsedMs: 10.0);
        EngineMeter.RecordPhaseTransition(EnginePhase.Applying, elapsedMs: 20.0);
        EngineMeter.RecordPayloadDownload(success: true, sizeBytes: 1000L, kind: EngineMeter.PayloadKind.Msi);
        EngineMeter.RecordPayloadDownload(success: false, sizeBytes: 0L, kind: EngineMeter.PayloadKind.Msi);
        EngineMeter.RecordRetry(EngineMeter.RetryOperation.Download);
        EngineMeter.RecordRetry(EngineMeter.RetryOperation.Download);
        EngineMeter.RecordError(ErrorKind.DownloadError);

        var snapshot = EngineMeter.Snapshot();

        // Two phase transitions recorded.
        Assert.Equal(2L, snapshot[EngineMeter.SnapshotKey.PhaseTransitions]);

        // One success + one failure download.
        Assert.Equal(1L, snapshot[EngineMeter.SnapshotKey.PayloadDownloadsSuccess]);
        Assert.Equal(1L, snapshot[EngineMeter.SnapshotKey.PayloadDownloadsFailure]);

        // Two retries.
        Assert.Equal(2L, snapshot[EngineMeter.SnapshotKey.Retries]);

        // One terminal error.
        Assert.Equal(1L, snapshot[EngineMeter.SnapshotKey.Errors]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2 — Snapshot after reset returns zeros
    // WHY: Reset must produce a clean baseline; otherwise sessions running
    //      in the same process accumulate stale counts from prior sessions.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Snapshot_AfterReset_ReturnsZeros()
    {
        // Drive some counters, then reset.
        EngineMeter.RecordRetry(EngineMeter.RetryOperation.Download);
        EngineMeter.ResetForTesting();

        var snapshot = EngineMeter.Snapshot();

        Assert.Equal(0L, snapshot[EngineMeter.SnapshotKey.PhaseTransitions]);
        Assert.Equal(0L, snapshot[EngineMeter.SnapshotKey.PayloadDownloadsSuccess]);
        Assert.Equal(0L, snapshot[EngineMeter.SnapshotKey.PayloadDownloadsFailure]);
        Assert.Equal(0L, snapshot[EngineMeter.SnapshotKey.Retries]);
        Assert.Equal(0L, snapshot[EngineMeter.SnapshotKey.Errors]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3 — FlushToLogger writes exactly one "Metrics" Info entry
    // WHY: Callers (EngineSession.DisposeAsync) need one structured log entry
    //      per session containing all counter values so log parsers can extract
    //      them without scanning for individual metric lines.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void FlushToLogger_WritesOneMetricsEntry()
    {
        EngineMeter.ResetForTesting();
        EngineMeter.RecordPhaseTransition(EnginePhase.Detecting, elapsedMs: 5.0);
        EngineMeter.RecordRetry(EngineMeter.RetryOperation.MsiInstall);

        var logger = new ListLogger();
        EngineMeter.FlushToLogger(logger);

        var entries = logger.Entries.Where(e => e.Category == "Metrics").ToArray();
        Assert.Single(entries);

        var entry = entries[0];
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.NotNull(entry.Properties);

        // Phase transitions = 1.
        Assert.True(entry.Properties!.ContainsKey(EngineMeter.SnapshotKey.PhaseTransitions));
        Assert.Equal("1", entry.Properties[EngineMeter.SnapshotKey.PhaseTransitions]);

        // Retries = 1.
        Assert.True(entry.Properties.ContainsKey(EngineMeter.SnapshotKey.Retries));
        Assert.Equal("1", entry.Properties[EngineMeter.SnapshotKey.Retries]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4 — FlushToLogger with all-zero snapshot still writes the entry
    // WHY: The absence of activity is itself diagnostic — a zero-download session
    //      means the engine never tried to fetch a payload, which operators need
    //      to distinguish from a download that succeeded.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void FlushToLogger_AllZeros_StillWritesEntry()
    {
        EngineMeter.ResetForTesting();

        var logger = new ListLogger();
        EngineMeter.FlushToLogger(logger);

        var entries = logger.Entries.Where(e => e.Category == "Metrics").ToArray();
        Assert.Single(entries);
        Assert.NotNull(entries[0].Properties);
        Assert.NotEmpty(entries[0].Properties!);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5 — EngineSession.DisposeAsync writes Metrics entry through the logger
    // WHY: The bridge is only useful if DisposeAsync actually calls FlushToLogger.
    //      This test exercises the full session teardown path via the test channel.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task EngineSession_DisposeAsync_WritesMetricsEntry()
    {
        EngineMeter.ResetForTesting();
        EngineMeter.RecordPhaseTransition(EnginePhase.Detecting, elapsedMs: 1.0);

        var logger = new ListLogger();
        var channel = new FakeUiChannel();
        var opts = new Engine.EngineSessionOptions { Logger = logger };

        await using (var session = EngineSession.BindToChannel(channel, opts))
        {
            // No pipeline run needed — dispose path is what we're testing.
        }

        var metricsEntries = logger.Entries.Where(e => e.Category == "Metrics").ToArray();
        Assert.Single(metricsEntries);
        Assert.NotNull(metricsEntries[0].Properties);
        Assert.True(metricsEntries[0].Properties!.ContainsKey(EngineMeter.SnapshotKey.PhaseTransitions));
    }
}
