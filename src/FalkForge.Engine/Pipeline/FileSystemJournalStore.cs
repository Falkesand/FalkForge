namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Journal;

/// <summary>
/// Production <see cref="IRollbackJournalStore"/> that persists journal entries to
/// disk via <see cref="RollbackJournal"/>. Each <see cref="Append"/> flushes to the
/// underlying file with <see cref="System.IO.FileOptions.WriteThrough"/> so entries
/// survive a process crash.
/// </summary>
public sealed class FileSystemJournalStore : IRollbackJournalStore
{
    private readonly string _journalPath;
    private RollbackJournal _journal;

    /// <summary>
    /// Opens (or creates) the journal file at <paramref name="journalPath"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the underlying journal file cannot be created.
    /// Callers should check the constructor does not throw; the path is validated lazily
    /// on first <see cref="Append"/> otherwise.
    /// </exception>
    public FileSystemJournalStore(string journalPath)
    {
        _journalPath = journalPath;
        _journal = new RollbackJournal(journalPath);
        var openResult = _journal.Open();
        if (openResult.IsFailure)
        {
            _journal.Dispose();
            throw new InvalidOperationException(
                $"Failed to open rollback journal at '{journalPath}': {openResult.Error.Message}");
        }
    }

    /// <inheritdoc/>
    public Result<Unit> Append(JournalEntry entry) => _journal.WriteEntry(entry);

    /// <inheritdoc/>
    /// <remarks>Returns entries accumulated in memory since construction or last <see cref="Clear"/>.</remarks>
    public Result<IReadOnlyList<JournalEntry>> LoadAll()
        => Result<IReadOnlyList<JournalEntry>>.Success(_journal.Entries);

    /// <inheritdoc/>
    /// <remarks>
    /// Disposes the current journal file, deletes it, and opens a fresh one at the
    /// same path so subsequent <see cref="Append"/> calls work normally.
    /// </remarks>
    public Result<Unit> Clear()
    {
        _journal.Dispose();

        try
        {
            if (File.Exists(_journalPath))
                File.Delete(_journalPath);
        }
        catch (IOException ex)
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError,
                $"Failed to delete journal file: {ex.Message}");
        }

        _journal = new RollbackJournal(_journalPath);
        var openResult = _journal.Open();
        return openResult.IsFailure
            ? Result<Unit>.Failure(openResult.Error)
            : Unit.Value;
    }

    /// <inheritdoc/>
    public void Dispose() => _journal.Dispose();
}
