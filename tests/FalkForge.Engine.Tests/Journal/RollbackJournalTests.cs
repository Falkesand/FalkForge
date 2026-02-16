namespace FalkForge.Engine.Tests.Journal;

using FalkForge.Engine.Journal;
using Xunit;

public sealed class RollbackJournalTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _journalPath;

    public RollbackJournalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkForge_JournalTest_{Guid.NewGuid():N}");
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

    [Fact]
    public void ReadEntries_RoundTrip_PreservesAllFields()
    {
        using (var journal = new RollbackJournal(_journalPath))
        {
            journal.Open();
            journal.WriteEntry(new JournalEntry
            {
                EntryType = JournalEntryType.MsiInstalled,
                Description = "Installed MSI package 'TestApp'",
                PackageId = "TestApp",
                PackageType = "MsiPackage",
                CachePath = @"C:\ProgramData\FalkForge\Cache\testapp.msi",
                ProductCode = "{12345678-1234-1234-1234-123456789ABC}",
                UninstallCommand = null
            });
            journal.WriteEntry(new JournalEntry
            {
                EntryType = JournalEntryType.ExeInstalled,
                Description = "Installed EXE package 'Helper'",
                UndoData = new byte[] { 0xDE, 0xAD },
                PackageId = "Helper",
                PackageType = "ExePackage",
                CachePath = @"C:\ProgramData\FalkForge\Cache\helper.exe",
                ProductCode = null,
                UninstallCommand = @"""C:\Program Files\Helper\uninstall.exe"" /silent"
            });
            journal.WriteEntry(new JournalEntry
            {
                EntryType = JournalEntryType.PayloadCached,
                Description = "Cached payload",
                PackageId = "Payload1",
                PackageType = null,
                CachePath = @"C:\ProgramData\FalkForge\Cache\payload.cab",
                ProductCode = null,
                UninstallCommand = null
            });
        }

        var readResult = RollbackJournal.ReadEntries(_journalPath);

        Assert.True(readResult.IsSuccess);
        var entries = readResult.Value;
        Assert.Equal(3, entries.Length);

        // Entry 0: MsiInstalled
        Assert.Equal(JournalEntryType.MsiInstalled, entries[0].EntryType);
        Assert.Equal("Installed MSI package 'TestApp'", entries[0].Description);
        Assert.Equal("TestApp", entries[0].PackageId);
        Assert.Equal("MsiPackage", entries[0].PackageType);
        Assert.Equal(@"C:\ProgramData\FalkForge\Cache\testapp.msi", entries[0].CachePath);
        Assert.Equal("{12345678-1234-1234-1234-123456789ABC}", entries[0].ProductCode);
        Assert.Null(entries[0].UninstallCommand);
        Assert.Null(entries[0].UndoData);

        // Entry 1: ExeInstalled
        Assert.Equal(JournalEntryType.ExeInstalled, entries[1].EntryType);
        Assert.Equal("Installed EXE package 'Helper'", entries[1].Description);
        Assert.Equal("Helper", entries[1].PackageId);
        Assert.Equal("ExePackage", entries[1].PackageType);
        Assert.Equal(@"C:\ProgramData\FalkForge\Cache\helper.exe", entries[1].CachePath);
        Assert.Null(entries[1].ProductCode);
        Assert.Equal(@"""C:\Program Files\Helper\uninstall.exe"" /silent", entries[1].UninstallCommand);
        Assert.Equal(new byte[] { 0xDE, 0xAD }, entries[1].UndoData);

        // Entry 2: PayloadCached
        Assert.Equal(JournalEntryType.PayloadCached, entries[2].EntryType);
        Assert.Equal("Cached payload", entries[2].Description);
        Assert.Equal("Payload1", entries[2].PackageId);
        Assert.Null(entries[2].PackageType);
        Assert.Equal(@"C:\ProgramData\FalkForge\Cache\payload.cab", entries[2].CachePath);
        Assert.Null(entries[2].ProductCode);
        Assert.Null(entries[2].UninstallCommand);
    }

    [Fact]
    public void WriteEntry_MsiInstalled_WithoutProductCode_ReturnsValidationFailure()
    {
        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        var result = journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.MsiInstalled,
            Description = "Test",
            PackageId = "Pkg1",
            ProductCode = null
        });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("ProductCode", result.Error.Message);
    }

    [Fact]
    public void WriteEntry_ExeInstalled_WithoutUninstallCommand_ReturnsValidationFailure()
    {
        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        var result = journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.ExeInstalled,
            Description = "Test",
            PackageId = "Pkg1",
            UninstallCommand = null
        });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("UninstallCommand", result.Error.Message);
    }

    [Fact]
    public void WriteEntry_PayloadCached_WithoutCachePath_ReturnsValidationFailure()
    {
        using var journal = new RollbackJournal(_journalPath);
        journal.Open();

        var result = journal.WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.PayloadCached,
            Description = "Test",
            PackageId = "Pkg1",
            CachePath = null
        });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("CachePath", result.Error.Message);
    }
}
