namespace FalkForge.Testing;

using FalkForge.Engine.Journal;
using FalkForge.Engine.Pipeline;

/// <summary>
/// In-memory <see cref="IRollbackJournalStore"/> for tests. Entries are stored in
/// a list; no disk I/O occurs. Thread-safe via a lock.
/// </summary>
public sealed class InMemoryJournalStore : IRollbackJournalStore
{
    private readonly List<JournalEntry> _entries = [];
    private readonly Lock _lock = new();

    /// <summary>All entries appended since construction or last <see cref="Clear"/>.</summary>
    public IReadOnlyList<JournalEntry> Entries
    {
        get { lock (_lock) { return _entries.ToArray(); } }
    }

    /// <inheritdoc/>
    public Result<Unit> Append(JournalEntry entry)
    {
        lock (_lock) { _entries.Add(entry); }
        return Unit.Value;
    }

    /// <inheritdoc/>
    public Result<IReadOnlyList<JournalEntry>> LoadAll()
    {
        lock (_lock)
        {
            return Result<IReadOnlyList<JournalEntry>>.Success(_entries.ToArray());
        }
    }

    /// <inheritdoc/>
    public Result<Unit> Clear()
    {
        lock (_lock) { _entries.Clear(); }
        return Unit.Value;
    }

    /// <inheritdoc/>
    public void Dispose() { }
}
