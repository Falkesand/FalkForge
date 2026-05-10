namespace FalkForge.Engine.Tests.Journal;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Journal.UndoOperations;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class RollbackExecutorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _journalPath;

    public RollbackExecutorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkForge_RollbackExecTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _journalPath = Path.Combine(_tempDir, "test.journal");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
    }

    [Fact]
    public async Task Execute_EmptyEntries_ReturnsSuccess()
    {
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync([], CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Execute_EntriesProcessedInReverseOrder()
    {
        var recorder = new RecordingUndoOperation();
        var executor = new RollbackExecutor([recorder]);

        var entries = new JournalEntry[]
        {
            CreateMsiEntry("PkgA", "{11111111-1111-1111-1111-111111111111}"),
            CreateMsiEntry("PkgB", "{22222222-2222-2222-2222-222222222222}"),
            CreateMsiEntry("PkgC", "{33333333-3333-3333-3333-333333333333}")
        };

        await executor.ExecuteAsync(entries, CancellationToken.None);

        Assert.Equal(3, recorder.ExecutedEntries.Count);
        Assert.Equal("PkgC", recorder.ExecutedEntries[0].PackageId);
        Assert.Equal("PkgB", recorder.ExecutedEntries[1].PackageId);
        Assert.Equal("PkgA", recorder.ExecutedEntries[2].PackageId);
    }

    [Fact]
    public async Task Execute_MsiEntry_CallsMsiexec()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = CreateExecutor(runner);

        var entries = new JournalEntry[]
        {
            CreateMsiEntry("TestMsi", "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}")
        };

        var result = await executor.ExecuteAsync(entries, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("msiexec.exe", runner.LastFileName);
        Assert.Equal("/x {AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE} /qn /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task Execute_ExeEntry_CallsUninstallCommand()
    {
        var tempExe = Path.GetTempFileName();
        try
        {
            var runner = new MockProcessRunner().WithExitCode(0);
            var executor = CreateExecutor(runner);

            var entries = new JournalEntry[]
            {
                CreateExeEntry("TestExe", $"\"{tempExe}\" /silent")
            };

            var result = await executor.ExecuteAsync(entries, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(tempExe, runner.LastFileName);
            Assert.Equal("/silent", runner.LastArguments);
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Fact]
    public async Task Execute_CacheEntry_DeletesFile()
    {
        var tempFile = Path.Combine(_tempDir, "cached.msi");
        File.WriteAllText(tempFile, "test content");
        Assert.True(File.Exists(tempFile));

        var executor = CreateExecutor(allowedCacheRoot: _tempDir);

        var entries = new JournalEntry[]
        {
            CreateCacheEntry("CachedPkg", tempFile)
        };

        var result = await executor.ExecuteAsync(entries, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(tempFile));
    }

    [Fact]
    public async Task Execute_FailedUndo_ContinuesWithRemainingEntries()
    {
        var recorder = new RecordingUndoOperation(failOnPackageId: "PkgB");
        var executor = new RollbackExecutor([recorder]);

        var entries = new JournalEntry[]
        {
            CreateMsiEntry("PkgA", "{11111111-1111-1111-1111-111111111111}"),
            CreateMsiEntry("PkgB", "{22222222-2222-2222-2222-222222222222}"),
            CreateMsiEntry("PkgC", "{33333333-3333-3333-3333-333333333333}")
        };

        var result = await executor.ExecuteAsync(entries, CancellationToken.None);

        // All 3 entries were attempted despite PkgB failing
        Assert.Equal(3, recorder.ExecutedEntries.Count);
        // Result reflects that there was an error
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.RollbackError, result.Error.Kind);
        Assert.Contains("1 error(s)", result.Error.Message);
    }

    [Fact]
    public async Task Execute_MultipleFailures_ReportsAllErrors()
    {
        var recorder = new RecordingUndoOperation(failOnPackageId: "PkgA", alsoFailOnPackageId: "PkgC");
        var executor = new RollbackExecutor([recorder]);

        var entries = new JournalEntry[]
        {
            CreateMsiEntry("PkgA", "{11111111-1111-1111-1111-111111111111}"),
            CreateMsiEntry("PkgB", "{22222222-2222-2222-2222-222222222222}"),
            CreateMsiEntry("PkgC", "{33333333-3333-3333-3333-333333333333}")
        };

        var result = await executor.ExecuteAsync(entries, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("2 error(s)", result.Error.Message);
    }

    [Fact]
    public async Task Execute_SkipsSegmentBoundaryEntries()
    {
        var recorder = new RecordingUndoOperation();
        var executor = new RollbackExecutor([recorder]);

        var entries = new JournalEntry[]
        {
            new() { EntryType = JournalEntryType.SegmentBoundary, Description = "Seg1" },
            CreateMsiEntry("PkgA", "{11111111-1111-1111-1111-111111111111}"),
        };

        await executor.ExecuteAsync(entries, CancellationToken.None);

        // Only the MSI entry is processed, not the segment boundary
        Assert.Single(recorder.ExecutedEntries);
        Assert.Equal("PkgA", recorder.ExecutedEntries[0].PackageId);
    }

    [Fact]
    public async Task Execute_UnhandledEntryType_IsSkipped()
    {
        var executor = CreateExecutor();

        var entries = new JournalEntry[]
        {
            new()
            {
                EntryType = JournalEntryType.RegistryKeyCreated,
                Description = "Created registry key"
            }
        };

        var result = await executor.ExecuteAsync(entries, CancellationToken.None);

        // Unhandled types are skipped, not treated as errors
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Execute_LogsEachRollbackAction()
    {
        var logger = new CapturingLogger();
        var runner = new MockProcessRunner().WithExitCode(0);
        var undoOps = CreateDefaultUndoOps(runner);
        var executor = new RollbackExecutor(undoOps, logger);

        var entries = new JournalEntry[]
        {
            CreateMsiEntry("TestMsi", "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}")
        };

        await executor.ExecuteAsync(entries, CancellationToken.None);

        Assert.Contains(logger.Messages, l => l.Contains("Rolling back:"));
    }

    [Fact]
    public async Task Execute_LogsEmptyJournal()
    {
        var logger = new CapturingLogger();
        var executor = new RollbackExecutor([], logger);

        await executor.ExecuteAsync([], CancellationToken.None);

        Assert.Contains(logger.Messages, l => l.Contains("No journal entries"));
    }

    [Fact]
    public async Task ExecuteSegment_RollsBackOnlySegmentEntries()
    {
        var recorder = new RecordingUndoOperation();
        var executor = new RollbackExecutor([recorder]);

        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        journal.BeginSegment("Seg1");
        journal.WriteEntry(CreateMsiEntry("PkgA", "{11111111-1111-1111-1111-111111111111}"));

        journal.BeginSegment("Seg2");
        journal.WriteEntry(CreateMsiEntry("PkgB", "{22222222-2222-2222-2222-222222222222}"));
        journal.WriteEntry(CreateMsiEntry("PkgC", "{33333333-3333-3333-3333-333333333333}"));

        // Only roll back Seg2
        var result = await executor.ExecuteSegmentAsync(journal, "Seg2", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, recorder.ExecutedEntries.Count);
        // Reversed order
        Assert.Equal("PkgC", recorder.ExecutedEntries[0].PackageId);
        Assert.Equal("PkgB", recorder.ExecutedEntries[1].PackageId);
    }

    [Fact]
    public async Task ExecuteSegment_Seg1Untouched_WhenSeg2Fails()
    {
        var recorder = new RecordingUndoOperation();
        var executor = new RollbackExecutor([recorder]);

        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        journal.BeginSegment("Seg1");
        journal.WriteEntry(CreateMsiEntry("PkgA", "{11111111-1111-1111-1111-111111111111}"));

        journal.BeginSegment("Seg2");
        journal.WriteEntry(CreateMsiEntry("PkgB", "{22222222-2222-2222-2222-222222222222}"));

        // Roll back only Seg2 — Seg1's PkgA should not be rolled back
        await executor.ExecuteSegmentAsync(journal, "Seg2", CancellationToken.None);

        Assert.Single(recorder.ExecutedEntries);
        Assert.Equal("PkgB", recorder.ExecutedEntries[0].PackageId);
    }

    [Fact]
    public async Task ExecuteSegment_NonexistentSegment_ReturnsSuccess()
    {
        var executor = CreateExecutor();

        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        journal.BeginSegment("Seg1");
        journal.WriteEntry(CreateMsiEntry("PkgA", "{11111111-1111-1111-1111-111111111111}"));

        var result = await executor.ExecuteSegmentAsync(journal, "NonExistent", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteSegment_EmptySegment_ReturnsSuccess()
    {
        var executor = CreateExecutor();

        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        journal.BeginSegment("EmptySeg");
        journal.BeginSegment("Seg2");

        var result = await executor.ExecuteSegmentAsync(journal, "EmptySeg", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Execute_MixedEntryTypes_DispatchesToCorrectOperations()
    {
        var tempFile = Path.Combine(_tempDir, "payload.msi");
        File.WriteAllText(tempFile, "cached payload");
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = CreateExecutor(runner, allowedCacheRoot: _tempDir);

        var entries = new JournalEntry[]
        {
            CreateCacheEntry("CachePkg", tempFile),
            CreateMsiEntry("MsiPkg", "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}"),
        };

        var result = await executor.ExecuteAsync(entries, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // MSI was processed last in list but first in reverse — so it was the last call to runner
        // Cache was first in list but last in reverse
        Assert.False(File.Exists(tempFile));
    }

    [Fact]
    public async Task Execute_AllSucceed_ReturnsSuccess()
    {
        var tempExe = Path.GetTempFileName();
        try
        {
            var runner = new MockProcessRunner().WithExitCode(0);
            var executor = CreateExecutor(runner);

            var entries = new JournalEntry[]
            {
                CreateMsiEntry("PkgA", "{11111111-1111-1111-1111-111111111111}"),
                CreateExeEntry("PkgB", $"\"{tempExe}\" /quiet"),
            };

            var result = await executor.ExecuteAsync(entries, CancellationToken.None);

            Assert.True(result.IsSuccess);
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Fact]
    public async Task ExecuteSegment_LogsSegmentInfo()
    {
        var logger = new CapturingLogger();
        var executor = new RollbackExecutor([], logger);

        using var journal = new RollbackJournal(_journalPath);
        journal.Open();
        journal.BeginSegment("TestSeg");

        await executor.ExecuteSegmentAsync(journal, "TestSeg", CancellationToken.None);

        Assert.Contains(logger.Messages, l => l.Contains("Rolling back segment 'TestSeg'"));
    }

    // --- Helper methods ---

    private static RollbackExecutor CreateExecutor(MockProcessRunner? runner = null, string? allowedCacheRoot = null)
    {
        runner ??= new MockProcessRunner().WithExitCode(0);
        return new RollbackExecutor(CreateDefaultUndoOps(runner, allowedCacheRoot));
    }

    private static IUndoOperation[] CreateDefaultUndoOps(IProcessRunner runner, string? allowedCacheRoot = null)
    {
        var cacheOp = allowedCacheRoot is not null
            ? new CacheCleanupOperation(allowedCacheRoot)
            : new CacheCleanupOperation();

        return
        [
            new MsiUninstallOperation(runner),
            new ExeRollbackOperation(runner),
            cacheOp
        ];
    }

    private static JournalEntry CreateMsiEntry(string packageId, string productCode) => new()
    {
        EntryType = JournalEntryType.MsiInstalled,
        Description = $"Installed MSI package '{packageId}'",
        PackageId = packageId,
        PackageType = "MsiPackage",
        ProductCode = productCode
    };

    private static JournalEntry CreateExeEntry(string packageId, string uninstallCommand) => new()
    {
        EntryType = JournalEntryType.ExeInstalled,
        Description = $"Installed EXE package '{packageId}'",
        PackageId = packageId,
        PackageType = "ExePackage",
        UninstallCommand = uninstallCommand
    };

    private static JournalEntry CreateCacheEntry(string packageId, string cachePath) => new()
    {
        EntryType = JournalEntryType.PayloadCached,
        Description = $"Cached payload for '{packageId}'",
        PackageId = packageId,
        CachePath = cachePath
    };

    /// <summary>
    /// A recording undo operation that tracks which entries were executed,
    /// with optional failure simulation for specific package IDs.
    /// </summary>
    private sealed class RecordingUndoOperation : IUndoOperation
    {
        private readonly string? _failOnPackageId;
        private readonly string? _alsoFailOnPackageId;

        public List<JournalEntry> ExecutedEntries { get; } = [];

        public RecordingUndoOperation(
            string? failOnPackageId = null,
            string? alsoFailOnPackageId = null)
        {
            _failOnPackageId = failOnPackageId;
            _alsoFailOnPackageId = alsoFailOnPackageId;
        }

        public bool CanHandle(JournalEntry entry) =>
            entry.EntryType is JournalEntryType.MsiInstalled
                or JournalEntryType.ExeInstalled
                or JournalEntryType.PayloadCached;

        public Task<Result<Unit>> ExecuteAsync(JournalEntry entry, CancellationToken ct)
        {
            ExecutedEntries.Add(entry);

            if (entry.PackageId == _failOnPackageId || entry.PackageId == _alsoFailOnPackageId)
            {
                return Task.FromResult(
                    Result<Unit>.Failure(ErrorKind.RollbackError,
                        $"Simulated failure for {entry.PackageId}"));
            }

            return Task.FromResult<Result<Unit>>(Unit.Value);
        }
    }

    /// <summary>
    /// A simple IEngineLogger implementation that captures log messages for test assertions.
    /// </summary>
    private sealed class CapturingLogger : IEngineLogger
    {
        public List<string> Messages { get; } = [];

        public LogLevel MinimumLevel { get; set; } = LogLevel.Verbose;
        public void SetMinimumLevel(LogLevel level) => MinimumLevel = level;
        public Guid SessionCorrelationId { get; set; }

        public void Log(LogLevel level, string category, string message, IReadOnlyDictionary<string, string>? properties = null)
            => Messages.Add(message);

        public void Verbose(string category, string message) => Messages.Add(message);
        public void Debug(string category, string message) => Messages.Add(message);
        public void Info(string category, string message) => Messages.Add(message);
        public void Warning(string category, string message) => Messages.Add(message);
        public void Error(string category, string message) => Messages.Add(message);
        public void Dispose() { }
    }
}
