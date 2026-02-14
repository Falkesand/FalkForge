namespace FalkInstaller.Engine.Tests.Journal;

using FalkInstaller.Engine.Journal;
using Xunit;

public sealed class RollbackJournalTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _journalPath;

    public RollbackJournalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkInstaller_JournalTest_{Guid.NewGuid():N}");
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
    public void Open_CreatesJournalFile()
    {
        using var journal = new RollbackJournal(_journalPath);

        var result = journal.Open();

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(_journalPath));
    }

    [Fact]
    public void WriteEntry_WithoutOpen_ReturnsFailure()
    {
        using var journal = new RollbackJournal(_journalPath);

        var result = journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.FileCreated,
            Description = "test"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.RollbackError, result.Error.Kind);
    }

    [Fact]
    public void WriteEntry_AddsEntryToList()
    {
        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "Installed test package"
        });

        Assert.Single(journal.Entries);
        Assert.Equal(JournalEntryType.PackageInstalled, journal.Entries[0].EntryType);
        Assert.Equal("Installed test package", journal.Entries[0].Description);
    }

    [Fact]
    public void ReadEntries_RoundTrip_PreservesEntries()
    {
        // Write entries
        using (var journal = new RollbackJournal(_journalPath))
        {
            journal.Open();
            journal.WriteEntry(new JournalEntry
            {
                EntryType = JournalEntryType.PackageInstalled,
                Description = "Installed Pkg1"
            });
            journal.WriteEntry(new JournalEntry
            {
                EntryType = JournalEntryType.FileCreated,
                Description = @"C:\Program Files\TestApp\test.dll"
            });
            journal.WriteEntry(new JournalEntry
            {
                EntryType = JournalEntryType.RegistryKeyCreated,
                Description = @"HKLM\SOFTWARE\TestApp"
            });
        }

        // Read entries back
        var readResult = RollbackJournal.ReadEntries(_journalPath);

        Assert.True(readResult.IsSuccess);
        var entries = readResult.Value;
        Assert.Equal(3, entries.Length);

        Assert.Equal(JournalEntryType.PackageInstalled, entries[0].EntryType);
        Assert.Equal("Installed Pkg1", entries[0].Description);

        Assert.Equal(JournalEntryType.FileCreated, entries[1].EntryType);
        Assert.Equal(@"C:\Program Files\TestApp\test.dll", entries[1].Description);

        Assert.Equal(JournalEntryType.RegistryKeyCreated, entries[2].EntryType);
        Assert.Equal(@"HKLM\SOFTWARE\TestApp", entries[2].Description);
    }

    [Fact]
    public void ReadEntries_WithUndoData_PreservesData()
    {
        var undoData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF };

        using (var journal = new RollbackJournal(_journalPath))
        {
            journal.Open();
            journal.WriteEntry(new JournalEntry
            {
                EntryType = JournalEntryType.RegistryValueSet,
                Description = "Set value",
                UndoData = undoData
            });
        }

        var readResult = RollbackJournal.ReadEntries(_journalPath);

        Assert.True(readResult.IsSuccess);
        Assert.Single(readResult.Value);
        Assert.NotNull(readResult.Value[0].UndoData);
        Assert.Equal(undoData, readResult.Value[0].UndoData);
    }

    [Fact]
    public void ReadEntries_WithNullUndoData_PreservesNull()
    {
        using (var journal = new RollbackJournal(_journalPath))
        {
            journal.Open();
            journal.WriteEntry(new JournalEntry
            {
                EntryType = JournalEntryType.FileCreated,
                Description = "Created file",
                UndoData = null
            });
        }

        var readResult = RollbackJournal.ReadEntries(_journalPath);

        Assert.True(readResult.IsSuccess);
        Assert.Null(readResult.Value[0].UndoData);
    }

    [Fact]
    public void ReadEntries_MissingFile_ReturnsFailure()
    {
        var result = RollbackJournal.ReadEntries(Path.Combine(_tempDir, "nonexistent.journal"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.RollbackError, result.Error.Kind);
        Assert.Contains("not found", result.Error.Message);
    }

    [Fact]
    public void ReadEntries_EmptyJournal_ReturnsEmptyArray()
    {
        // Create and immediately close an empty journal
        using (var journal = new RollbackJournal(_journalPath))
        {
            journal.Open();
        }

        var readResult = RollbackJournal.ReadEntries(_journalPath);

        Assert.True(readResult.IsSuccess);
        Assert.Empty(readResult.Value);
    }

    [Fact]
    public void WriteEntry_MultipleEntries_PreservesOrder()
    {
        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        for (var i = 0; i < 5; i++)
        {
            journal.WriteEntry(new JournalEntry
            {
                EntryType = JournalEntryType.FileCreated,
                Description = $"Entry {i}"
            });
        }

        Assert.Equal(5, journal.Entries.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal($"Entry {i}", journal.Entries[i].Description);
        }
    }

    [Fact]
    public void Open_CreatesDirectoryIfNotExists()
    {
        var nestedPath = Path.Combine(_tempDir, "sub1", "sub2", "test.journal");
        using var journal = new RollbackJournal(nestedPath);

        var result = journal.Open();

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(nestedPath));
    }
}
