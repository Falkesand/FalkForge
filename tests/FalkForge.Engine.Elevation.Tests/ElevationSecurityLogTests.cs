using System.Reflection;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests;

/// <summary>
/// Tests for <see cref="ElevationSecurityLog"/>.
///
/// ElevationSecurityLog is an internal static class with static fields, so
/// tests must isolate state via reflection. Each test uses a fresh temp file
/// and resets the static _writer / _initialized fields before and after.
///
/// All tests in this class are serialized via [Collection] to prevent
/// concurrent mutation of the shared static state.
/// </summary>
[Collection("ElevationSecurityLog")]
public sealed class ElevationSecurityLogTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Reflection helpers
    // -------------------------------------------------------------------------

    private static readonly FieldInfo WriterField =
        typeof(ElevationSecurityLog).GetField(
            "_writer",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("_writer field not found on ElevationSecurityLog");

    private static readonly FieldInfo InitializedField =
        typeof(ElevationSecurityLog).GetField(
            "_initialized",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("_initialized field not found on ElevationSecurityLog");

    private readonly string _tempDir;
    private StreamWriter? _activeWriter;
    private string? _logFilePath;

    public ElevationSecurityLogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ElevationLogTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        ResetStaticState();
    }

    public void Dispose()
    {
        ResetStaticState();

        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup — temp files are not critical
        }
    }

    /// <summary>
    /// Shuts down any existing writer, then injects a fresh StreamWriter over
    /// a new temp file. Returns the file path for assertion.
    /// </summary>
    private string InjectFreshWriter()
    {
        // Shut down any prior writer cleanly
        ElevationSecurityLog.Shutdown();

        _logFilePath = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.log");
        var fileStream = new FileStream(_logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _activeWriter = new StreamWriter(fileStream, System.Text.Encoding.UTF8) { AutoFlush = true };

        WriterField.SetValue(null, _activeWriter);
        InitializedField.SetValue(null, true);

        return _logFilePath;
    }

    /// <summary>
    /// Returns the injected writer cleanly and resets static state.
    /// </summary>
    private void ResetStaticState()
    {
        // Close any active writer
        try
        {
            var writer = WriterField.GetValue(null) as StreamWriter;
            writer?.Flush();
            writer?.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }

        WriterField.SetValue(null, null);
        InitializedField.SetValue(null, false);
    }

    /// <summary>
    /// Reads all lines written to the injected log file. Flushes the writer first
    /// so any buffered content is visible (AutoFlush=true so this is redundant but safe).
    /// </summary>
    private string[] ReadLogLines()
    {
        _activeWriter?.Flush();

        // Release the writer so we can open the file for reading
        ElevationSecurityLog.Shutdown();
        _activeWriter = null;

        // Re-read without re-injecting (further writes would no-op)
        return File.ReadAllLines(_logFilePath!);
    }

    // -------------------------------------------------------------------------
    // Format tests
    // -------------------------------------------------------------------------

    [Fact]
    public void SecurityEvent_WritesTabDelimitedLineWithWarningSeverity()
    {
        var logPath = InjectFreshWriter();

        ElevationSecurityLog.SecurityEvent("ParentWatch", "PID recycling detected");

        var lines = ReadLogLines();

        // Exactly one line after injection (Initialize writes an INFO line at
        // real startup, but here we inject directly, so only our entry exists)
        Assert.Single(lines);
        var parts = lines[0].Split('\t');
        Assert.Equal(4, parts.Length);

        // [0] timestamp — parseable ISO-8601
        Assert.True(DateTimeOffset.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _),
            $"Timestamp not parseable: '{parts[0]}'");

        // [1] level
        Assert.Equal("WARNING", parts[1]);

        // [2] category
        Assert.Equal("ParentWatch", parts[2]);

        // [3] message
        Assert.Equal("PID recycling detected", parts[3]);
    }

    [Fact]
    public void Error_WritesTabDelimitedLineWithErrorSeverity()
    {
        InjectFreshWriter();

        ElevationSecurityLog.Error("Connection", "Pipe connect failed");

        var lines = ReadLogLines();

        Assert.Single(lines);
        var parts = lines[0].Split('\t');
        Assert.Equal(4, parts.Length);
        Assert.Equal("ERROR", parts[1]);
        Assert.Equal("Connection", parts[2]);
        Assert.Equal("Pipe connect failed", parts[3]);
    }

    [Fact]
    public void Info_WritesTabDelimitedLineWithInfoSeverity()
    {
        InjectFreshWriter();

        ElevationSecurityLog.Info("Startup", "Elevation process started");

        var lines = ReadLogLines();

        Assert.Single(lines);
        var parts = lines[0].Split('\t');
        Assert.Equal(4, parts.Length);
        Assert.Equal("INFO", parts[1]);
        Assert.Equal("Startup", parts[2]);
        Assert.Equal("Elevation process started", parts[3]);
    }

    [Fact]
    public void MultipleWrites_AppendNotOverwrite()
    {
        InjectFreshWriter();

        ElevationSecurityLog.Info("A", "first");
        ElevationSecurityLog.SecurityEvent("B", "second");
        ElevationSecurityLog.Error("C", "third");

        var lines = ReadLogLines();

        Assert.Equal(3, lines.Length);
        Assert.Contains("first", lines[0]);
        Assert.Contains("second", lines[1]);
        Assert.Contains("third", lines[2]);
    }

    [Fact]
    public void Timestamp_IsIso8601UtcRoundTrip()
    {
        InjectFreshWriter();

        var before = DateTime.UtcNow.AddSeconds(-1);
        ElevationSecurityLog.Info("Clock", "timestamp check");
        var after = DateTime.UtcNow.AddSeconds(1);

        var lines = ReadLogLines();
        var timestamp = DateTimeOffset.Parse(lines[0].Split('\t')[0],
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(timestamp.UtcDateTime >= before, "Timestamp before test start");
        Assert.True(timestamp.UtcDateTime <= after, "Timestamp after test end");
    }

    // -------------------------------------------------------------------------
    // Shutdown / no-op tests
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteAfterShutdown_DoesNotThrow()
    {
        InjectFreshWriter();
        ElevationSecurityLog.Shutdown(); // closes writer → _writer = null

        // All writes should silently no-op
        var ex = Record.Exception(() =>
        {
            ElevationSecurityLog.SecurityEvent("Test", "should be silently dropped");
            ElevationSecurityLog.Error("Test", "should be silently dropped");
            ElevationSecurityLog.Info("Test", "should be silently dropped");
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Shutdown_CalledTwice_DoesNotThrow()
    {
        InjectFreshWriter();

        var ex = Record.Exception(() =>
        {
            ElevationSecurityLog.Shutdown();
            ElevationSecurityLog.Shutdown(); // second call: _writer already null
        });

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Initialize idempotency
    // -------------------------------------------------------------------------

    [Fact]
    public void Initialize_CalledTwice_OnlyOpensOneFile()
    {
        // First Initialize creates a real log file
        ElevationSecurityLog.Initialize();

        // Capture first writer reference
        var firstWriter = WriterField.GetValue(null);

        // Second Initialize must be a no-op — same writer still in place
        ElevationSecurityLog.Initialize();

        var secondWriter = WriterField.GetValue(null);

        Assert.Same(firstWriter, secondWriter);

        // Teardown
        ElevationSecurityLog.Shutdown();
    }

    // -------------------------------------------------------------------------
    // Path containment / traversal guard
    // -------------------------------------------------------------------------

    [Fact]
    public void Initialize_LogFileResidedInFalkForgeTempSubdirectory()
    {
        ElevationSecurityLog.Initialize();

        // Snapshot the writer's underlying stream path before shutdown
        var writer = WriterField.GetValue(null) as StreamWriter;
        Assert.NotNull(writer);

        // Reach the FileStream through the StreamWriter's BaseStream
        var baseStream = writer.BaseStream as FileStream;
        Assert.NotNull(baseStream);

        var logPath = Path.GetFullPath(baseStream.Name);
        var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FalkForge"));

        Assert.StartsWith(expectedRoot, logPath, StringComparison.OrdinalIgnoreCase);

        ElevationSecurityLog.Shutdown();
    }

    // -------------------------------------------------------------------------
    // Concurrency test
    // -------------------------------------------------------------------------

    [Fact]
    public void ConcurrentWrites_AllLinesCompleteAndCountMatches()
    {
        const int threadCount = 8;
        const int writesPerThread = 50;
        const int expectedLines = threadCount * writesPerThread;

        InjectFreshWriter();

        Parallel.For(0, threadCount, threadIndex =>
        {
            for (var i = 0; i < writesPerThread; i++)
            {
                ElevationSecurityLog.SecurityEvent(
                    $"Thread{threadIndex}",
                    $"message {i}");
            }
        });

        var lines = ReadLogLines();

        // Correct total line count
        Assert.Equal(expectedLines, lines.Length);

        // Every line is a complete 4-field tab-delimited record — no interleaving
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            Assert.True(parts.Length == 4,
                $"Line has {parts.Length} fields (expected 4), indicating interleaving: '{line}'");

            // Timestamp field must be parseable
            Assert.True(DateTimeOffset.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _),
                $"Non-parseable timestamp in line: '{line}'");

            // Level must be WARNING (all writes via SecurityEvent)
            Assert.Equal("WARNING", parts[1]);
        }
    }

    // -------------------------------------------------------------------------
    // Bucket D: edge case — Initialize failure degrades to no-op
    // -------------------------------------------------------------------------

    [Fact]
    public void Initialize_WhenLogFileCannotBeCreated_DoesNotThrow()
    {
        // To force Initialize to fail, place a file at the path it would use as a
        // directory. Directory.CreateDirectory on a path that is already a file will
        // throw an IOException, which the catch block must swallow silently.
        //
        // We cannot predict the exact filename (it embeds a timestamp + PID), but we
        // CAN create a file named "FalkForge" in %TEMP%, which prevents
        // Directory.CreateDirectory(%TEMP%\FalkForge) from succeeding.
        //
        // Note: this test modifies the %TEMP%\FalkForge path, so it must clean up
        // carefully and restore the real directory when done.

        var falkForgePath = Path.Combine(Path.GetTempPath(), "FalkForge");
        var wasDirectory = Directory.Exists(falkForgePath);
        var wasFile = File.Exists(falkForgePath);

        // Only proceed with the blocking-file approach if the path does not already
        // exist as a directory (to avoid clobbering a real log dir on a machine
        // that has previously run the elevation process).
        if (wasDirectory || wasFile)
        {
            // Path already exists in some form — the failure scenario cannot be safely
            // reproduced without risk of data loss. Skip by verifying Initialize is
            // robust when called with _initialized=false and no writer.
            ResetStaticState();
            var ex = Record.Exception(() => ElevationSecurityLog.SecurityEvent("Skipped", "path blocked test"));
            Assert.Null(ex); // no-op write must not throw
            return;
        }

        // Create a FILE at the directory path to block Directory.CreateDirectory
        File.WriteAllText(falkForgePath, "blocking");
        try
        {
            ResetStaticState();

            // Initialize should swallow the IOException from Directory.CreateDirectory
            var ex = Record.Exception(ElevationSecurityLog.Initialize);
            Assert.Null(ex);

            // After a failed Initialize, writes must no-op silently
            var writeEx = Record.Exception(() =>
            {
                ElevationSecurityLog.SecurityEvent("Test", "post-failure write");
                ElevationSecurityLog.Error("Test", "post-failure write 2");
                ElevationSecurityLog.Info("Test", "post-failure write 3");
            });
            Assert.Null(writeEx);
        }
        finally
        {
            // Remove the blocking file and ensure ElevationSecurityLog state is clean
            ElevationSecurityLog.Shutdown();
            ResetStaticState();
            try { File.Delete(falkForgePath); } catch { /* best-effort */ }
        }
    }

    // -------------------------------------------------------------------------
    // Bucket D: edge case — tab character in message body
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteEntry_WhenMessageContainsTabChar_DelimiterStaysParseable()
    {
        // The log format is tab-delimited: [timestamp]\t[level]\t[category]\t[message].
        // If the message itself contains a tab, a naive Split('\t') on the full line
        // yields more than 4 fields. The format must remain parseable: the first three
        // delimiters mark the fixed fields; everything from field index 3 onward is
        // the message body.
        //
        // This test verifies that:
        //  (a) no exception is thrown when writing a tab-containing message, AND
        //  (b) the first 3 fields are still correctly recoverable (timestamp, level, category),
        //      and the message body can be recovered by joining the remaining fields.
        //
        // If the format is found to be ambiguous (field[3] alone is not the full message),
        // this test documents the boundary and the consumer must use a 4-field limit on Split.

        InjectFreshWriter();

        const string tabMessage = "a\tb\tc"; // three sub-fields separated by tabs
        ElevationSecurityLog.SecurityEvent("TabTest", tabMessage);

        var lines = ReadLogLines();
        Assert.Single(lines);

        var line = lines[0];

        // Split with no limit — will produce more than 4 parts if message has tabs
        var allParts = line.Split('\t');
        Assert.True(allParts.Length >= 4,
            "Line must have at least 4 tab-delimited segments.");

        // Fields 0-2 are always fixed (timestamp, level, category)
        Assert.True(DateTimeOffset.TryParse(allParts[0],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _),
            $"Field 0 (timestamp) not parseable: '{allParts[0]}'");
        Assert.Equal("WARNING", allParts[1]);
        Assert.Equal("TabTest", allParts[2]);

        // Field 3 onward is the message body — joining recovers the original text
        var recoveredMessage = string.Join('\t', allParts[3..]);
        Assert.Equal(tabMessage, recoveredMessage);
    }
}
