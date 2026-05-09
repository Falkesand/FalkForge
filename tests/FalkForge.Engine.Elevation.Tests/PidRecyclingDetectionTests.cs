using System.Diagnostics;
using System.Reflection;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests;

/// <summary>
/// Tests PID-recycling detection in <see cref="ElevatedHost.IsParentAlive"/>.
///
/// ElevatedHost captures the parent's start time at construction and compares it
/// on every liveness check. If a different process has been given the same PID
/// (recycled), the start time will differ and IsParentAlive must return false.
///
/// Because System.Diagnostics.Process is a sealed class with no interface, we
/// cannot mock it. Instead we use reflection to overwrite the captured
/// _parentStartTime field after construction, simulating the scenario where the
/// host was created against process A but now process B owns that PID.
/// </summary>
[Collection("ElevationSecurityLog")]
public sealed class PidRecyclingDetectionTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ElevatedHost CreateHostForCurrentProcess()
    {
        var options = new PipeConnectionOptions
        {
            PipeName = "test-pipe-recycling",
            SharedSecret = new byte[] { 1, 2, 3 }
        };
        var executor = new ElevatedCommandExecutor(Array.Empty<MockCommand>());
        return new ElevatedHost(options, Environment.ProcessId, executor);
    }

    /// <summary>
    /// Overwrite the private _parentStartTime field via reflection.
    /// This simulates what happens when PID is recycled: the field holds the
    /// original process's start time but the live process has a different one.
    /// </summary>
    private static void InjectStartTime(ElevatedHost host, DateTime fakeStartTime)
    {
        var field = typeof(ElevatedHost).GetField(
            "_parentStartTime",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_parentStartTime field not found on ElevatedHost");

        field.SetValue(host, fakeStartTime);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void IsParentAlive_PidExistsButStartTimeDiffers_ReturnsFalse()
    {
        // Arrange: construct host against current process (so PID exists),
        // then inject a start time that differs from the real one.
        var host = CreateHostForCurrentProcess();
        InjectStartTime(host, DateTime.MinValue); // guaranteed to differ

        // Act & Assert
        Assert.False(host.IsParentAlive(),
            "IsParentAlive must return false when start time does not match " +
            "(PID recycling scenario).");
    }

    [Fact]
    public void IsParentAlive_PidExistsAndStartTimeMatches_ReturnsTrue()
    {
        // Arrange: construct normally — captured start time matches the real one.
        var host = CreateHostForCurrentProcess();

        // Act & Assert
        Assert.True(host.IsParentAlive(),
            "IsParentAlive must return true when PID and start time both match.");
    }

    [Fact]
    public void IsParentAlive_FutureStartTimeInjected_ReturnsFalse()
    {
        // Arrange: inject a start time far in the future — also a recycling mismatch.
        var host = CreateHostForCurrentProcess();
        InjectStartTime(host, DateTime.MaxValue);

        // Act & Assert
        Assert.False(host.IsParentAlive(),
            "IsParentAlive must return false when injected start time is in the future.");
    }

    [Fact]
    public void IsParentAlive_StartTimeOffByOneSecond_ReturnsFalse()
    {
        // Arrange: off-by-one-second mismatch — ensure no clock-skew tolerance.
        var host = CreateHostForCurrentProcess();
        var realStartTime = Process.GetCurrentProcess().StartTime;
        InjectStartTime(host, realStartTime.AddSeconds(1));

        // Act & Assert
        Assert.False(host.IsParentAlive(),
            "IsParentAlive must return false for any start-time discrepancy, " +
            "even a one-second difference (no clock-skew tolerance).");
    }

    [Fact]
    public void IsParentAlive_NonExistentPid_ReturnsFalse()
    {
        // Covered by ParentExitDetectionTests but included here for completeness
        // of the recycling test class.
        var options = new PipeConnectionOptions
        {
            PipeName = "test-pipe-recycling",
            SharedSecret = new byte[] { 1, 2, 3 }
        };
        var executor = new ElevatedCommandExecutor(Array.Empty<MockCommand>());
        var host = new ElevatedHost(options, 99998, executor);

        Assert.False(host.IsParentAlive());
    }

    [Fact]
    public void IsParentAlive_PidExistsButStartTimeDiffers_LogsSecurityEvent()
    {
        // When PID recycling is detected, ElevatedHost.IsParentAlive must log a
        // WARNING security event via ElevationSecurityLog containing context that
        // identifies the recycled PID (defense-in-depth audit trail).
        //
        // Technique: inject a StreamWriter over a temp file before the call, then
        // read the file back and assert the security entry was written.

        var tempDir = Path.Combine(Path.GetTempPath(), $"PidRecycleLogTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var logFile = Path.Combine(tempDir, "test.log");

        var writerField = typeof(ElevationSecurityLog).GetField(
            "_writer", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_writer field not found");
        var initializedField = typeof(ElevationSecurityLog).GetField(
            "_initialized", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_initialized field not found");

        // Shut down any previously active log state
        ElevationSecurityLog.Shutdown();
        StreamWriter? injectedWriter = null;

        try
        {
            // Inject fresh writer so we can capture what IsParentAlive logs
            var fs = new FileStream(logFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            injectedWriter = new StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            writerField.SetValue(null, injectedWriter);
            initializedField.SetValue(null, true);

            // Arrange: construct host against current process, then inject a mismatched
            // start time to trigger the recycling detection branch.
            var host = CreateHostForCurrentProcess();
            InjectStartTime(host, DateTime.MinValue); // guaranteed mismatch

            // Act
            var alive = host.IsParentAlive();

            // Primary assertion: the recycling check returned false
            Assert.False(alive);

            // Flush and close before reading
            ElevationSecurityLog.Shutdown();
            injectedWriter = null;

            // Secondary assertion: a security event was written to the log file
            var lines = File.ReadAllLines(logFile);
            Assert.True(lines.Length > 0, "Expected at least one log entry for PID-recycling event.");

            // The entry must contain "WARNING" severity and reference the PID
            Assert.True(
                lines.Any(l => l.Contains("WARNING")),
                "Expected a WARNING-level log entry from the PID-recycling detection code path.");

            // The message should identify the affected PID to support incident investigation
            var currentPid = Environment.ProcessId.ToString();
            Assert.True(
                lines.Any(l => l.Contains(currentPid)),
                $"Expected the log entry to reference the PID ({currentPid}) involved in the recycling scenario.");
        }
        finally
        {
            // Ensure log state is clean for subsequent tests in the collection
            ElevationSecurityLog.Shutdown();
            injectedWriter?.Dispose();
            writerField.SetValue(null, null);
            initializedField.SetValue(null, false);

            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
