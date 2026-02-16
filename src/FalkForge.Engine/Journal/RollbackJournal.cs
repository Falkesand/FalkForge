namespace FalkForge.Engine.Journal;

public sealed class RollbackJournal : IDisposable
{
    private readonly string _journalPath;
    private readonly List<JournalEntry> _entries = new();
    private FileStream? _stream;
    private BinaryWriter? _writer;

    public RollbackJournal(string journalPath)
    {
        _journalPath = journalPath;
    }

    public IReadOnlyList<JournalEntry> Entries => _entries.AsReadOnly();

    public Result<Unit> Open()
    {
        try
        {
            var dir = Path.GetDirectoryName(_journalPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            _stream = new FileStream(_journalPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new BinaryWriter(_stream);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError, $"Failed to create journal: {ex.Message}");
        }
    }

    public Result<Unit> WriteEntry(JournalEntry entry)
    {
        if (_writer is null)
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError, "Journal not open");
        }

        // Validate required fields based on entry type
        if (entry.EntryType == JournalEntryType.MsiInstalled && string.IsNullOrWhiteSpace(entry.ProductCode))
        {
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"MsiInstalled entry requires ProductCode (package '{entry.PackageId}')");
        }

        if (entry.EntryType == JournalEntryType.ExeInstalled && string.IsNullOrWhiteSpace(entry.UninstallCommand))
        {
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"ExeInstalled entry requires UninstallCommand (package '{entry.PackageId}')");
        }

        if (entry.EntryType == JournalEntryType.PayloadCached && string.IsNullOrWhiteSpace(entry.CachePath))
        {
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"PayloadCached entry requires CachePath (package '{entry.PackageId}')");
        }

        try
        {
            _writer.Write((int)entry.EntryType);
            _writer.Write(entry.Description);
            var hasUndoData = entry.UndoData is not null;
            _writer.Write(hasUndoData);
            if (hasUndoData)
            {
                _writer.Write(entry.UndoData!.Length);
                _writer.Write(entry.UndoData);
            }

            // Write additional fields (empty string for null to support round-trip)
            _writer.Write(entry.PackageId ?? string.Empty);
            _writer.Write(entry.PackageType ?? string.Empty);
            _writer.Write(entry.CachePath ?? string.Empty);
            _writer.Write(entry.ProductCode ?? string.Empty);
            _writer.Write(entry.UninstallCommand ?? string.Empty);

            _writer.Flush();
            _entries.Add(entry);
            return Unit.Value;
        }
        catch (IOException ex)
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError, $"Failed to write journal entry: {ex.Message}");
        }
    }

    public Result<Unit> BeginSegment(string boundaryId)
    {
        return WriteEntry(new JournalEntry
        {
            EntryType = JournalEntryType.SegmentBoundary,
            Description = boundaryId
        });
    }

    public IReadOnlyList<JournalEntry> GetSegmentEntries(string boundaryId)
    {
        var result = new List<JournalEntry>();
        var inSegment = false;

        foreach (var entry in _entries)
        {
            if (entry.EntryType == JournalEntryType.SegmentBoundary)
            {
                if (entry.Description == boundaryId)
                {
                    inSegment = true;
                    continue;
                }

                if (inSegment)
                {
                    // Hit the next segment boundary, stop
                    break;
                }
            }
            else if (inSegment)
            {
                result.Add(entry);
            }
        }

        return result.AsReadOnly();
    }

    public static Result<JournalEntry[]> ReadEntries(string journalPath)
    {
        if (!File.Exists(journalPath))
        {
            return Result<JournalEntry[]>.Failure(ErrorKind.RollbackError, "Journal file not found");
        }

        try
        {
            using var stream = File.OpenRead(journalPath);
            using var reader = new BinaryReader(stream);
            var entries = new List<JournalEntry>();

            while (stream.Position < stream.Length)
            {
                var entryType = (JournalEntryType)reader.ReadInt32();
                var description = reader.ReadString();
                var hasUndoData = reader.ReadBoolean();
                byte[]? undoData = null;
                if (hasUndoData)
                {
                    var length = reader.ReadInt32();
                    undoData = reader.ReadBytes(length);
                }

                // Read additional fields (empty string becomes null)
                var packageId = NullIfEmpty(reader.ReadString());
                var packageType = NullIfEmpty(reader.ReadString());
                var cachePath = NullIfEmpty(reader.ReadString());
                var productCode = NullIfEmpty(reader.ReadString());
                var uninstallCommand = NullIfEmpty(reader.ReadString());

                entries.Add(new JournalEntry
                {
                    EntryType = entryType,
                    Description = description,
                    UndoData = undoData,
                    PackageId = packageId,
                    PackageType = packageType,
                    CachePath = cachePath,
                    ProductCode = productCode,
                    UninstallCommand = uninstallCommand
                });
            }

            return entries.ToArray();
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            return Result<JournalEntry[]>.Failure(ErrorKind.RollbackError, $"Failed to read journal: {ex.Message}");
        }
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _stream?.Dispose();
    }
}
