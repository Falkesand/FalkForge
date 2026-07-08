namespace FalkForge.Engine.Journal;

using FalkForge.Diagnostics;
using FalkForge.Engine.Journal.UndoOperations;

public sealed class RollbackExecutor
{
    private readonly IReadOnlyList<IUndoOperation> _undoOperations;
    private readonly IFalkLogger? _logger;

    public RollbackExecutor(IReadOnlyList<IUndoOperation> undoOperations, IFalkLogger? logger = null)
    {
        _undoOperations = undoOperations;
        _logger = logger;
    }

    /// <summary>
    /// Executes rollback for the given journal entries in reverse order.
    /// Best-effort: if one undo fails, the error is logged and remaining entries are still processed.
    /// </summary>
    public async Task<Result<Unit>> ExecuteAsync(IReadOnlyList<JournalEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            _logger?.Info("Rollback", "No journal entries to roll back");
            return Unit.Value;
        }

        var errors = new List<string>();

        // Process entries in reverse order (undo most recent changes first)
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];

            // Skip segment boundaries — they are structural markers, not undoable actions
            if (entry.EntryType == JournalEntryType.SegmentBoundary)
            {
                continue;
            }

            var operation = FindOperation(entry);
            if (operation is null)
            {
                _logger?.Info("Rollback", $"No undo operation registered for entry type {entry.EntryType} ('{entry.Description}')");
                continue;
            }

            _logger?.Info("Rollback", $"Rolling back: {entry.Description}");

            var result = await operation.ExecuteAsync(entry, ct);
            if (result.IsFailure)
            {
                var errorMsg = $"Rollback failed for '{entry.Description}': {result.Error.Message}";
                _logger?.Info("Rollback", errorMsg);
                errors.Add(errorMsg);
                // Best-effort: continue with remaining entries
            }
        }

        if (errors.Count > 0)
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError,
                $"Rollback completed with {errors.Count} error(s): {string.Join("; ", errors)}");
        }

        return Unit.Value;
    }

    /// <summary>
    /// Executes rollback for a specific segment identified by boundaryId.
    /// Reads segment entries from the journal and rolls them back in reverse order.
    /// </summary>
    public async Task<Result<Unit>> ExecuteSegmentAsync(
        RollbackJournal journal,
        string boundaryId,
        CancellationToken ct)
    {
        var segmentEntries = journal.GetSegmentEntries(boundaryId);
        _logger?.Info("Rollback", $"Rolling back segment '{boundaryId}' ({segmentEntries.Count} entries)");
        return await ExecuteAsync(segmentEntries, ct);
    }

    private IUndoOperation? FindOperation(JournalEntry entry)
    {
        for (var i = 0; i < _undoOperations.Count; i++)
        {
            if (_undoOperations[i].CanHandle(entry))
            {
                return _undoOperations[i];
            }
        }

        return null;
    }
}
