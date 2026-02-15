namespace FalkInstaller.Engine.Tests.Journal;

using FalkInstaller.Engine.Journal;
using Xunit;

public sealed class RollbackJournalSegmentTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _journalPath;

    public RollbackJournalSegmentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkInstaller_SegmentTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _journalPath = Path.Combine(_tempDir, "test.journal");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void BeginSegment_WritesSegmentBoundaryEntry()
    {
        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        var result = journal.BeginSegment("Segment1");

        Assert.True(result.IsSuccess);
        Assert.Single(journal.Entries);
        Assert.Equal(JournalEntryType.SegmentBoundary, journal.Entries[0].EntryType);
        Assert.Equal("Segment1", journal.Entries[0].Description);
    }

    [Fact]
    public void GetSegmentEntries_ReturnsOnlyEntriesInSegment()
    {
        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        journal.BeginSegment("Seg1");
        journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "Installed Pkg1"
        });
        journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.FileCreated,
            Description = "Created file1"
        });

        journal.BeginSegment("Seg2");
        journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "Installed Pkg2"
        });

        var seg1Entries = journal.GetSegmentEntries("Seg1");
        Assert.Equal(2, seg1Entries.Count);
        Assert.Equal("Installed Pkg1", seg1Entries[0].Description);
        Assert.Equal("Created file1", seg1Entries[1].Description);

        var seg2Entries = journal.GetSegmentEntries("Seg2");
        Assert.Single(seg2Entries);
        Assert.Equal("Installed Pkg2", seg2Entries[0].Description);
    }

    [Fact]
    public void GetEntries_ReturnsAllEntries_BackwardsCompatibility()
    {
        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        journal.BeginSegment("Seg1");
        journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "Installed Pkg1"
        });

        journal.BeginSegment("Seg2");
        journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "Installed Pkg2"
        });

        // Entries returns ALL entries including segment boundaries
        var allEntries = journal.Entries;
        Assert.Equal(4, allEntries.Count); // 2 boundaries + 2 package entries
    }

    [Fact]
    public void GetSegmentEntries_NonexistentSegment_ReturnsEmpty()
    {
        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        journal.BeginSegment("Seg1");
        journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "Installed Pkg1"
        });

        var entries = journal.GetSegmentEntries("NonExistent");
        Assert.Empty(entries);
    }

    [Fact]
    public void GetSegmentEntries_EmptySegment_ReturnsEmpty()
    {
        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        journal.BeginSegment("Seg1");
        journal.BeginSegment("Seg2");
        journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "Installed Pkg1"
        });

        var seg1Entries = journal.GetSegmentEntries("Seg1");
        Assert.Empty(seg1Entries);
    }
}
