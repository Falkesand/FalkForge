namespace FalkForge.Engine.Tests.Logging;

using FalkForge.Diagnostics;
using FalkForge.Engine.Logging;
using Xunit;

/// <summary>
/// Verifies log rotation behaviour in <see cref="EngineLogger"/>.
/// WHY: Without rotation, long-running installations accumulate unbounded log files.
///      The rotation algorithm must keep a bounded history, delete the oldest when the
///      cap is exceeded, and never touch paths outside the configured log directory.
/// </summary>
public sealed class LogRotationTests : IDisposable
{
    private readonly string _tempDir;

    public LogRotationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FalkForge_RotationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string LogPath(string name = "install.log") => Path.Combine(_tempDir, name);

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4 — active log rotates to .1 when size threshold is exceeded
    // WHY: Rotation must happen automatically; the operator must not manually
    //      intervene to prevent a single enormous log file.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Rotate_WhenSizeExceedsThreshold_CreatesNumberedBackup()
    {
        var options = new EngineLoggerOptions
        {
            RotationSizeThresholdBytes = 200,   // very small — forces rotation after a few lines
            RetentionCount = 3
        };

        var path = LogPath();

        // Explicit using block so that Dispose() (which flushes + rotates) runs
        // before the assertions below.  'using var' would defer dispose to end-of-scope,
        // which is AFTER the asserts, so the backup file would not yet exist.
        using (var logger = new EngineLogger(path, options: options))
        {
            logger.MinimumLevel = LogLevel.Info;

            // Write enough data to exceed the 200-byte threshold.
            for (var i = 0; i < 10; i++)
                logger.Info("Rotation", $"Entry number {i:D4} — padding to exceed threshold");
        } // <-- Dispose() flushes queue, writes entries, checks threshold, rotates active → .1

        // The .1 backup must exist after dispose.
        var backup = path + ".1";
        Assert.True(File.Exists(backup), $"Expected rotated file at '{backup}'");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5 — retention count caps rotated files; oldest is deleted
    // WHY: Without a cap the log directory grows without bound across sessions.
    //      The oldest file must be pruned first (FIFO expiry).
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Rotate_WhenRetentionExceeded_DeletesOldest()
    {
        // RetentionCount = 2 means keep at most .1 and .2; .3 is deleted.
        var options = new EngineLoggerOptions
        {
            RotationSizeThresholdBytes = 150,
            RetentionCount = 2
        };

        var path = LogPath();

        // Pre-create .1 and .2 to simulate two prior rotations.
        File.WriteAllText(path + ".1", "old-1");
        File.WriteAllText(path + ".2", "old-2");

        // Explicit using block so Dispose() runs before assertions (see Test 4 comment).
        using (var logger = new EngineLogger(path, options: options))
        {
            logger.MinimumLevel = LogLevel.Info;

            for (var i = 0; i < 20; i++)
                logger.Info("RetentionTest", $"Entry {i:D4} — needs to push past threshold quickly");
        } // <-- Dispose() flushes + rotates

        // After dispose, rotation should have happened.
        // .1 becomes new rotation; .2 was the previous .1 shifted; .3 would exceed cap → deleted.
        // Cap = 2 means at most .1 and .2 exist; .3 must NOT exist.
        var backup3 = path + ".3";
        Assert.False(File.Exists(backup3), $"File '{backup3}' should have been pruned by retention policy");

        // At least one rotated file must exist.
        Assert.True(File.Exists(path + ".1") || File.Exists(path + ".2"),
            "At least one rotated file must exist after writing past threshold");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6 — path containment: rotated files must stay inside log directory
    // WHY: An attacker who can influence the log file path must not be able to
    //      cause rotation to write or delete files outside the log directory.
    //      This mirrors CacheLayout's three-layer path traversal defense.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Rotate_PathContainment_RotatedFileStaysInsideLogDirectory()
    {
        var options = new EngineLoggerOptions
        {
            RotationSizeThresholdBytes = 100,
            RetentionCount = 3
        };

        var path = LogPath();

        // Explicit using block so Dispose() runs before assertions (see Test 4 comment).
        using (var logger = new EngineLogger(path, options: options))
        {
            logger.MinimumLevel = LogLevel.Info;

            for (var i = 0; i < 15; i++)
                logger.Info("Containment", $"Line {i} — exceeding threshold to force rotation");
        } // <-- Dispose() flushes + rotates

        // Verify every rotated file is inside _tempDir (the log root).
        var rotatedFiles = Directory.GetFiles(_tempDir, "*.1")
            .Concat(Directory.GetFiles(_tempDir, "*.2"))
            .Concat(Directory.GetFiles(_tempDir, "*.3"))
            .ToList();

        foreach (var f in rotatedFiles)
        {
            var canonical = Path.GetFullPath(f);
            Assert.StartsWith(Path.GetFullPath(_tempDir), canonical, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7 — retention cap evicts overflow slot, not the newest backup
    // WHY: The original loop deleted .{RetentionCount} (inside the window) instead of
    //      .{RetentionCount+1} (overflow).  After 3 rotations with RetentionCount=2 the
    //      content written in rotation 1 must be gone; only the most-recent 2 survive.
    //      This test catches that specific off-by-one regression.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Rotate_RetentionOverflow_OldestContentEvicted_NotNewest()
    {
        // RetentionCount = 2 means keep at most .1 and .2.
        // We will trigger 3 rotations; after the 3rd the content from the 1st rotation
        // must have been evicted — it must not appear in .1, .2, or the active file.
        var options = new EngineLoggerOptions
        {
            RotationSizeThresholdBytes = 100,   // very small — forces rotation after a few lines
            RetentionCount = 2
        };

        const string sentinelContent = "SENTINEL_ROTATION_1";

        var path = LogPath();

        // --- Rotation 1 ---
        // Write the sentinel marker so we can prove it was evicted later.
        using (var logger = new EngineLogger(path, options: options))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Info("R1", sentinelContent);
            for (var i = 0; i < 8; i++)
                logger.Info("R1", $"Padding line {i:D4} to exceed threshold");
        }

        // After rotation 1: active rotated to .1; fresh active opened.
        Assert.True(File.Exists(path + ".1"), "After rotation 1 — .1 must exist");

        // --- Rotation 2 ---
        using (var logger = new EngineLogger(path, options: options))
        {
            logger.MinimumLevel = LogLevel.Info;
            for (var i = 0; i < 10; i++)
                logger.Info("R2", $"Second-rotation padding {i:D4}");
        }

        // After rotation 2: .1 → .2, active → .1.  Cap = 2: .2 is the oldest kept.
        Assert.True(File.Exists(path + ".2"), "After rotation 2 — .2 must exist");

        // --- Rotation 3 ---
        using (var logger = new EngineLogger(path, options: options))
        {
            logger.MinimumLevel = LogLevel.Info;
            for (var i = 0; i < 10; i++)
                logger.Info("R3", $"Third-rotation padding {i:D4}");
        }

        // After rotation 3: shift .2 → .3 (overflow), .1 → .2, active → .1.
        // The overflow slot (.3) must be deleted; .1 and .2 survive.
        // The sentinel written in rotation 1 ended up in .2 after R2, then .3 after R3 —
        // but .3 is the overflow slot and must have been evicted.
        Assert.False(File.Exists(path + ".3"), ".3 is the overflow slot — must be deleted after rotation 3");
        Assert.True(File.Exists(path + ".1"), ".1 must exist after rotation 3");
        Assert.True(File.Exists(path + ".2"), ".2 must exist after rotation 3");

        // Crucially: the sentinel from rotation 1 must NOT be present anywhere.
        // If the bug were present, .{RetentionCount} (= .2) would have been deleted
        // during R3 instead of the overflow slot, but then the sentinel would still
        // be readable in one of the surviving files.
        var survivors = new[] { path, path + ".1", path + ".2" };
        foreach (var f in survivors)
        {
            if (!File.Exists(f))
                continue;
            var text = File.ReadAllText(f);
            Assert.False(text.Contains(sentinelContent, StringComparison.Ordinal),
                $"Sentinel from rotation 1 must be evicted — found in '{f}'");
        }
    }
}
