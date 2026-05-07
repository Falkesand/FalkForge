namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Journal;
using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// Tests for FileSystemJournalStore — the IRollbackJournalStore adapter wrapping RollbackJournal.
/// RED: fails until FileSystemJournalStore exists.
/// </summary>
public sealed class FileSystemJournalStoreTests : IDisposable
{
    private readonly string _tempPath;

    public FileSystemJournalStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"jtest_{Guid.NewGuid():N}.journal");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    private static JournalEntry MakeEntry(string description = "test") => new()
    {
        EntryType = JournalEntryType.PackageInstalled,
        Description = description,
        PackageId = "pkg1"
    };

    // ──────────────────────────────────────────────────────────────────────────
    // Append + LoadAll round-trip
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Append_Single_Entry_And_LoadAll_Returns_It()
    {
        using var store = new FileSystemJournalStore(_tempPath);
        var entry = MakeEntry("install pkg1");

        var appendResult = store.Append(entry);
        Assert.True(appendResult.IsSuccess);

        var loadResult = store.LoadAll();
        Assert.True(loadResult.IsSuccess);
        Assert.Single(loadResult.Value);
        Assert.Equal("install pkg1", loadResult.Value[0].Description);
    }

    [Fact]
    public void Append_Multiple_Preserves_Order()
    {
        using var store = new FileSystemJournalStore(_tempPath);
        for (var i = 0; i < 5; i++)
            Assert.True(store.Append(MakeEntry($"entry-{i}")).IsSuccess);

        var entries = store.LoadAll().Value;
        Assert.Equal(5, entries.Count);
        for (var i = 0; i < 5; i++)
            Assert.Equal($"entry-{i}", entries[i].Description);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Clear
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_After_Appends_Returns_Empty_LoadAll()
    {
        using var store = new FileSystemJournalStore(_tempPath);
        store.Append(MakeEntry("a"));
        store.Append(MakeEntry("b"));

        var clearResult = store.Clear();
        Assert.True(clearResult.IsSuccess);

        var entries = store.LoadAll().Value;
        Assert.Empty(entries);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Crash-recovery: entries survive Dispose + re-open via ReadEntries
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Entries_Are_Durable_Across_Dispose_And_Reopen()
    {
        // Write entries in first store instance (simulates normal session)
        using (var store = new FileSystemJournalStore(_tempPath))
        {
            store.Append(MakeEntry("before-crash"));
        }

        // Simulate crash recovery: read raw entries from file (RollbackJournal.ReadEntries)
        var readResult = RollbackJournal.ReadEntries(_tempPath);
        Assert.True(readResult.IsSuccess);
        Assert.Single(readResult.Value);
        Assert.Equal("before-crash", readResult.Value[0].Description);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MsiInstalled validation pass-through
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Append_MsiInstalled_Without_ProductCode_Returns_Failure()
    {
        using var store = new FileSystemJournalStore(_tempPath);
        var badEntry = new JournalEntry
        {
            EntryType = JournalEntryType.MsiInstalled,
            Description = "missing product code",
            PackageId = "pkg2"
            // ProductCode intentionally omitted
        };

        var result = store.Append(badEntry);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Implements IRollbackJournalStore (interface assignment)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FileSystemJournalStore_Implements_IRollbackJournalStore()
    {
        using IRollbackJournalStore store = new FileSystemJournalStore(_tempPath);
        Assert.NotNull(store);
    }
}
