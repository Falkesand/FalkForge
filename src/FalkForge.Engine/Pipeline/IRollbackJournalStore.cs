namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Journal;

/// <summary>
/// Durable storage port for the rollback journal. Hides on-disk format, binary layout,
/// and flush semantics from phase-step code.
/// </summary>
public interface IRollbackJournalStore : IDisposable
{
    /// <summary>
    /// Appends a single journal entry. Implementations must flush to durable storage
    /// before returning so that a process crash after a successful append leaves the
    /// entry readable by <see cref="LoadAll"/>.
    /// </summary>
    Result<Unit> Append(JournalEntry entry);

    /// <summary>
    /// Returns all entries written since the last <see cref="Clear"/> call, in
    /// append order. Used during crash-recovery rollback replay.
    /// </summary>
    Result<IReadOnlyList<JournalEntry>> LoadAll();

    /// <summary>
    /// Removes all entries. Called after a successful rollback or a clean completion
    /// to prevent stale recovery on the next run.
    /// </summary>
    Result<Unit> Clear();
}
