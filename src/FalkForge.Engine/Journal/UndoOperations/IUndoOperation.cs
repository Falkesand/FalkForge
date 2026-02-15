namespace FalkForge.Engine.Journal.UndoOperations;

public interface IUndoOperation
{
    bool CanHandle(JournalEntry entry);
    Task<Result<Unit>> ExecuteAsync(JournalEntry entry, CancellationToken ct);
}
