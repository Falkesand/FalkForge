namespace FalkForge.Engine.Tests.Logging;

using System.Collections.Concurrent;
using System.Globalization;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using Xunit;

public sealed class EngineLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public EngineLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FalkForge_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string GetLogPath(string name = "test.log") => Path.Combine(_tempDir, name);

    [Fact]
    public void Log_WritesEntryToFile()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Info("TestCategory", "Hello world");
        }

        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
        Assert.Contains("TestCategory", lines[0]);
        Assert.Contains("Hello world", lines[0]);
    }

    [Fact]
    public void Log_WritesIso8601Timestamp()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Info("Timestamp", "Check format");
        }

        var lines = File.ReadAllLines(path);
        Assert.Single(lines);

        var parts = lines[0].Split('\t');
        Assert.True(parts.Length >= 4, "Expected at least 4 tab-separated columns");

        // ISO 8601 format: 2026-02-15T12:34:56.7890000+00:00
        var parsed = DateTimeOffset.TryParseExact(
            parts[0], "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        Assert.True(parsed, $"Timestamp '{parts[0]}' is not valid ISO 8601 'o' format");
    }

    [Fact]
    public void Log_TabSeparatedFormat()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Info("Cat", "Msg");
        }

        var lines = File.ReadAllLines(path);
        var parts = lines[0].Split('\t');

        // Format: {ISO8601}\t{LEVEL}\t{CATEGORY}\t{MESSAGE}\t{PROPERTIES_JSON_OR_EMPTY}\t{SESSION_CORRELATION_ID}
        Assert.Equal(6, parts.Length);
        Assert.Equal("INFO", parts[1]);
        Assert.Equal("Cat", parts[2]);
        Assert.Equal("Msg", parts[3]);
        Assert.Equal(string.Empty, parts[4]);
        Assert.Equal(string.Empty, parts[5]); // no correlationId set = empty
    }

    [Fact]
    public void MinimumLevel_FiltersLowerLevels()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Warning;
            logger.Debug("Filtered", "Should not appear");
            logger.Info("Filtered", "Should not appear");
            logger.Warning("Kept", "Should appear");
            logger.Error("Kept", "Should also appear");
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("WARNING", lines[0]);
        Assert.Contains("ERROR", lines[1]);
    }

    [Fact]
    public void MinimumLevel_VerboseAllowsEverything()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Verbose;
            logger.Verbose("V", "verbose msg");
            logger.Debug("D", "debug msg");
            logger.Info("I", "info msg");
            logger.Warning("W", "warning msg");
            logger.Error("E", "error msg");
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(5, lines.Length);
    }

    [Fact]
    public void VerboseMethod_MapsToVerboseLevel()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Verbose;
            logger.Verbose("Cat", "msg");
        }

        var lines = File.ReadAllLines(path);
        var parts = lines[0].Split('\t');
        Assert.Equal("VERBOSE", parts[1]);
    }

    [Fact]
    public void DebugMethod_MapsToDebugLevel()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Debug;
            logger.Debug("Cat", "msg");
        }

        var lines = File.ReadAllLines(path);
        var parts = lines[0].Split('\t');
        Assert.Equal("DEBUG", parts[1]);
    }

    [Fact]
    public void InfoMethod_MapsToInfoLevel()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Info("Cat", "msg");
        }

        var lines = File.ReadAllLines(path);
        var parts = lines[0].Split('\t');
        Assert.Equal("INFO", parts[1]);
    }

    [Fact]
    public void WarningMethod_MapsToWarningLevel()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Warning;
            logger.Warning("Cat", "msg");
        }

        var lines = File.ReadAllLines(path);
        var parts = lines[0].Split('\t');
        Assert.Equal("WARNING", parts[1]);
    }

    [Fact]
    public void ErrorMethod_MapsToErrorLevel()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Error;
            logger.Error("Cat", "msg");
        }

        var lines = File.ReadAllLines(path);
        var parts = lines[0].Split('\t');
        Assert.Equal("ERROR", parts[1]);
    }

    [Fact]
    public void Log_WithProperties_SerializesAsJson()
    {
        var path = GetLogPath();
        var props = new Dictionary<string, string>
        {
            ["PackageId"] = "MyApp",
            ["Version"] = "1.2.3"
        };

        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Log(LogLevel.Info, "Install", "Installing package", props);
        }

        var lines = File.ReadAllLines(path);
        var parts = lines[0].Split('\t');
        Assert.Equal(6, parts.Length);

        var json = parts[4];
        Assert.Contains("\"PackageId\":\"MyApp\"", json);
        Assert.Contains("\"Version\":\"1.2.3\"", json);
        Assert.StartsWith("{", json);
        Assert.EndsWith("}", json);
    }

    [Fact]
    public void Log_WithoutProperties_EmptyColumn()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Log(LogLevel.Info, "Cat", "Msg");
        }

        var lines = File.ReadAllLines(path);
        var parts = lines[0].Split('\t');
        Assert.Equal(string.Empty, parts[4]);
    }

    [Fact]
    public void Log_PropertiesWithSpecialChars_EscapedCorrectly()
    {
        var path = GetLogPath();
        var props = new Dictionary<string, string>
        {
            ["path"] = "C:\\Users\\test",
            ["quote"] = "He said \"hi\""
        };

        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Log(LogLevel.Info, "Cat", "Msg", props);
        }

        var lines = File.ReadAllLines(path);
        var json = lines[0].Split('\t')[4];
        Assert.Contains("C:\\\\Users\\\\test", json);
        Assert.Contains("He said \\\"hi\\\"", json);
    }

    [Fact]
    public async Task ConcurrentWrites_AllEntriesWritten()
    {
        var path = GetLogPath();
        const int threadCount = 8;
        const int entriesPerThread = 50;

        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Verbose;

            var tasks = new Task[threadCount];
            for (var t = 0; t < threadCount; t++)
            {
                var threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (var i = 0; i < entriesPerThread; i++)
                    {
                        logger.Log(LogLevel.Info, $"Thread{threadId}", $"Entry {i}");
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(threadCount * entriesPerThread, lines.Length);
    }

    [Fact]
    public void CategoryAndMessage_PreservedExactly()
    {
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Info("Engine.Phases.Detecting", "Detected 3 packages already installed");
        }

        var lines = File.ReadAllLines(path);
        var parts = lines[0].Split('\t');
        Assert.Equal("Engine.Phases.Detecting", parts[2]);
        Assert.Equal("Detected 3 packages already installed", parts[3]);
    }

    [Fact]
    public void PipeCallback_InvokedForEachEntry()
    {
        var path = GetLogPath();
        var received = new ConcurrentBag<LogEntry>();

        using (var logger = new EngineLogger(path, entry => received.Add(entry)))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Info("Cat1", "Msg1");
            logger.Warning("Cat2", "Msg2");
            logger.Error("Cat3", "Msg3");
        }

        Assert.Equal(3, received.Count);
        Assert.Contains(received, e => e.Category == "Cat1" && e.Level == LogLevel.Info);
        Assert.Contains(received, e => e.Category == "Cat2" && e.Level == LogLevel.Warning);
        Assert.Contains(received, e => e.Category == "Cat3" && e.Level == LogLevel.Error);
    }

    [Fact]
    public void PipeCallback_NotInvokedForFilteredEntries()
    {
        var path = GetLogPath();
        var received = new ConcurrentBag<LogEntry>();

        using (var logger = new EngineLogger(path, entry => received.Add(entry)))
        {
            logger.MinimumLevel = LogLevel.Warning;
            logger.Debug("Cat", "Should be filtered");
            logger.Info("Cat", "Should be filtered");
            logger.Warning("Cat", "Should pass");
        }

        Assert.Single(received);
        Assert.Equal(LogLevel.Warning, received.First().Level);
    }

    [Fact]
    public void Dispose_FlushesAllPendingEntries()
    {
        var path = GetLogPath();

        // Write many entries below the auto-flush threshold, then dispose
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            for (var i = 0; i < 5; i++)
            {
                logger.Info("Flush", $"Entry {i}");
            }
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(5, lines.Length);
    }

    [Fact]
    public void LogFile_CreatedAtSpecifiedPath()
    {
        var path = GetLogPath("custom_name.log");
        Assert.False(File.Exists(path));

        using (var logger = new EngineLogger(path))
        {
            logger.Info("Cat", "Msg");
        }

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void LogFile_CreatesDirectoryIfNotExists()
    {
        var nested = Path.Combine(_tempDir, "sub", "dir", "test.log");
        Assert.False(Directory.Exists(Path.GetDirectoryName(nested)));

        using (var logger = new EngineLogger(nested))
        {
            logger.Info("Cat", "Msg");
        }

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void NullLogger_DoesNothingWithoutErrors()
    {
        using var logger = new NullLogger();
        logger.MinimumLevel = LogLevel.Verbose;

        // None of these should throw
        logger.Verbose("Cat", "msg");
        logger.Debug("Cat", "msg");
        logger.Info("Cat", "msg");
        logger.Warning("Cat", "msg");
        logger.Error("Cat", "msg");
        logger.Log(LogLevel.Info, "Cat", "msg", new Dictionary<string, string> { ["key"] = "val" });
    }

    [Fact]
    public void NullLogger_ImplementsIEngineLogger()
    {
        IEngineLogger logger = new NullLogger();
        Assert.NotNull(logger);
        logger.Dispose();
    }

    [Fact]
    public void GetDefaultLogPath_ContainsFalkForgeDirectory()
    {
        var path = EngineLogger.GetDefaultLogPath();
        Assert.Contains("FalkForge", path);
        Assert.Contains("install_", path);
        Assert.EndsWith(".log", path);
    }

    [Fact]
    public void GetDefaultLogPath_ContainsPerSessionGuidDirectory()
    {
        var path1 = EngineLogger.GetDefaultLogPath();
        var path2 = EngineLogger.GetDefaultLogPath();

        // Each call should produce a unique session directory
        var dir1 = Path.GetDirectoryName(path1)!;
        var dir2 = Path.GetDirectoryName(path2)!;
        Assert.NotEqual(dir1, dir2);

        // The parent of the session dir should be FalkForge
        var falkForgeDir = Path.GetDirectoryName(dir1)!;
        Assert.EndsWith("FalkForge", falkForgeDir);
    }

    [Fact]
    public void MultipleDisposes_DoNotThrow()
    {
        var path = GetLogPath();
        var logger = new EngineLogger(path);
        logger.Info("Cat", "Msg");
        logger.Dispose();
        logger.Dispose(); // Should not throw
    }

    [Fact]
    public void Log_PropertiesWithControlChars_EscapedCorrectly()
    {
        var path = GetLogPath();
        var props = new Dictionary<string, string>
        {
            ["backspace"] = "a\bb",
            ["formfeed"] = "a\fb",
            ["null"] = "a\0b",
            ["bell"] = "a\ab",
            ["vtab"] = "a\vb"
        };

        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Log(LogLevel.Info, "Cat", "Msg", props);
        }

        var lines = File.ReadAllLines(path);
        var json = lines[0].Split('\t')[4];
        Assert.Contains("a\\bb", json);
        Assert.Contains("a\\fb", json);
        Assert.Contains("a\\u0000b", json);
        Assert.Contains("a\\u0007b", json);
        Assert.Contains("a\\u000Bb", json);
    }

    [Fact]
    public void ErrorLevel_FlushesImmediately()
    {
        var path = GetLogPath();
        using var logger = new EngineLogger(path);
        logger.MinimumLevel = LogLevel.Info;
        logger.Error("Critical", "Something went wrong");

        // File should be flushed immediately after Error, even before dispose.
        // Use FileShare.ReadWrite since the logger still holds the file open.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        var content = reader.ReadToEnd();
        Assert.Contains("Something went wrong", content);
    }

    [Fact]
    public void SessionCorrelationId_WrittenToLogFileEntries()
    {
        // WHY: Each log line must carry the session correlation id so that logs from
        // multiple concurrent processes (UI, Engine, Elevation) can be correlated.
        var path = GetLogPath();
        var correlationId = Guid.NewGuid();

        using (var logger = new EngineLogger(path))
        {
            logger.SessionCorrelationId = correlationId;
            logger.MinimumLevel = LogLevel.Info;
            logger.Info("Cat", "Correlated message");
        }

        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
        Assert.Contains(correlationId.ToString("D"), lines[0]);
    }

    [Fact]
    public void SetMinimumLevel_AppliesToSubsequentLogCalls()
    {
        // WHY: Runtime log-level changes must take effect immediately, without
        // requiring a logger restart. Pre-set Information; Debug must be filtered
        // out. After SetMinimumLevel(Debug), subsequent Debug entries must be kept.
        var path = GetLogPath();
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Debug("Before", "Should be filtered");
            logger.SetMinimumLevel(LogLevel.Debug);
            logger.Debug("After", "Should be kept");
        }

        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
        Assert.Contains("After", lines[0]);
        Assert.Contains("Should be kept", lines[0]);
        Assert.DoesNotContain("Before", string.Join("\n", lines));
    }

    [Fact]
    public void MinimumLevel_PropertyExposesCurrentValue()
    {
        // WHY: The MinimumLevel getter must return the currently effective level
        // (initial ctor-set, after property write, and after SetMinimumLevel).
        var path = GetLogPath();
        using var logger = new EngineLogger(path);
        logger.MinimumLevel = LogLevel.Info;
        Assert.Equal(LogLevel.Info, logger.MinimumLevel);

        logger.SetMinimumLevel(LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, logger.MinimumLevel);

        logger.SetMinimumLevel(LogLevel.Verbose);
        Assert.Equal(LogLevel.Verbose, logger.MinimumLevel);
    }

    [Fact]
    public async Task SetMinimumLevel_ConcurrentWithLogCalls_NoCorruption()
    {
        // WHY: SetMinimumLevel must publish the new value to other threads without
        // corrupting in-flight Log() writes. We spin up 3 writers and 1 level-cycler
        // for ~200ms; afterwards every line in the log file must parse as a
        // well-formed tab-separated entry (6 columns, ISO timestamp).
        var path = GetLogPath();
        var levels = new[] { LogLevel.Verbose, LogLevel.Debug, LogLevel.Info, LogLevel.Warning };

        using (var logger = new EngineLogger(path))
        {
            logger.SetMinimumLevel(LogLevel.Verbose);

            var stop = false;
            var writers = new Task[3];
            for (var w = 0; w < 3; w++)
            {
                var id = w;
                writers[w] = Task.Run(() =>
                {
                    while (!Volatile.Read(ref stop))
                    {
                        logger.Log(LogLevel.Info, $"W{id}", "concurrent message");
                    }
                });
            }

            var cycler = Task.Run(() =>
            {
                var i = 0;
                while (!Volatile.Read(ref stop))
                {
                    logger.SetMinimumLevel(levels[i++ % levels.Length]);
                }
            });

            await Task.Delay(200);
            Volatile.Write(ref stop, true);
            await Task.WhenAll(writers);
            await cycler;
        }

        // After dispose, every line must parse as an ISO timestamp + 6 tab-separated fields.
        var lines = File.ReadAllLines(path);
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            Assert.Equal(6, parts.Length);
            var ok = DateTimeOffset.TryParseExact(
                parts[0], "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            Assert.True(ok, $"Malformed timestamp on line: {line}");
            Assert.Equal("INFO", parts[1]);
        }
    }

    [Fact]
    public void Log_BelowMinimumLevel_DoesNotAllocate()
    {
        // WHY: Gate 6 (zero-waste) — when an entry is below the minimum level,
        // Log must early-return BEFORE constructing a LogEntry, calling
        // DateTimeOffset.UtcNow, or boxing the properties dictionary. We measure
        // managed allocation deltas via GC.GetTotalAllocatedBytes(precise: true).
        var path = GetLogPath();
        using var logger = new EngineLogger(path);
        logger.SetMinimumLevel(LogLevel.Warning);

        // Warm up JIT for the Log path so first-call jitting doesn't pollute deltas.
        for (var i = 0; i < 10; i++)
            logger.Log(LogLevel.Verbose, "Cat", "warmup");

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < 100; i++)
            logger.Log(LogLevel.Verbose, "Cat", "below-level message");
        var after = GC.GetTotalAllocatedBytes(precise: true);

        var delta = after - before;
        // Tight bound: 100 below-level calls should allocate ~0 bytes. Allow a
        // tiny margin (e.g. test-runner noise) but well below per-call LogEntry
        // construction cost (~64+ bytes).
        Assert.True(delta < 64, $"Below-level Log calls allocated {delta} bytes (expected ~0).");
    }

    [Fact]
    public void PipeCallback_EntryCarriesSessionCorrelationId()
    {
        // WHY: The pipe callback converts LogEntry to LogMessage for the UI; the
        // SessionCorrelationId on the entry must flow through to the wire message.
        var path = GetLogPath();
        var correlationId = Guid.NewGuid();
        LogEntry? captured = null;

        using (var logger = new EngineLogger(path, entry => captured = entry))
        {
            logger.SessionCorrelationId = correlationId;
            logger.MinimumLevel = LogLevel.Info;
            logger.Info("Cat", "Msg");
        }

        Assert.NotNull(captured);
        Assert.Equal(correlationId, captured!.Value.SessionCorrelationId);
    }

    [Fact]
    public void EngineLogger_CustomPath_WritesToCustomLocation()
    {
        var path = GetLogPath("custom-location.log");
        using (var logger = new EngineLogger(path))
        {
            logger.MinimumLevel = LogLevel.Info;
            logger.Info("Custom", "msg");
        }

        Assert.True(File.Exists(path), $"Expected log at {path}");
        var content = File.ReadAllText(path);
        Assert.Contains("Custom", content);
    }

    [Fact]
    public void EngineLogger_CustomMinimumLevel_FiltersBelowLevel()
    {
        var path = GetLogPath("filter-by-level.log");
        // Use the new ctor overload that accepts a starting minimum level.
        using (var logger = new EngineLogger(path, minimumLevel: LogLevel.Warning))
        {
            logger.Info("X", "should be dropped");
            logger.Warning("X", "should be kept");
        }

        var content = File.ReadAllText(path);
        Assert.DoesNotContain("should be dropped", content);
        Assert.Contains("should be kept", content);
    }
}
