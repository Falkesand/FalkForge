namespace FalkInstaller.Engine.Journal;

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

            _writer.Flush();
            _entries.Add(entry);
            return Unit.Value;
        }
        catch (IOException ex)
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError, $"Failed to write journal entry: {ex.Message}");
        }
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

                entries.Add(new JournalEntry
                {
                    EntryType = entryType,
                    Description = description,
                    UndoData = undoData
                });
            }

            return entries.ToArray();
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            return Result<JournalEntry[]>.Failure(ErrorKind.RollbackError, $"Failed to read journal: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _stream?.Dispose();
    }
}
