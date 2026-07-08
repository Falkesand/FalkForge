namespace FalkForge.Engine.Pipeline;

using System.Diagnostics;
using FalkForge.Diagnostics;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Journal.UndoOperations;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;

/// <summary>
/// Rollback phase step. Loads all journal entries from
/// <see cref="IRollbackJournalStore"/>, delegates undo execution to
/// <see cref="RollbackExecutor"/>, and clears the journal on completion.
/// Progress and outcome are reported via <see cref="IUiChannel"/>.
/// </summary>
internal sealed class RollbackStep : IRollbackStep
{
    private readonly IRollbackJournalStore _journalStore;
    private readonly IReadOnlyList<IUndoOperation> _undoOperations;
    private readonly IUiChannel _uiChannel;
    private readonly IFalkLogger? _logger;

    public RollbackStep(
        IRollbackJournalStore journalStore,
        IReadOnlyList<IUndoOperation> undoOperations,
        IUiChannel uiChannel,
        IFalkLogger? logger = null)
    {
        _journalStore = journalStore;
        _undoOperations = undoOperations;
        _uiChannel = uiChannel;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        var startTs = Stopwatch.GetTimestamp();
        try
        {
            return await ExecuteCoreAsync(ctx, ct);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
            EngineMeter.RecordPhaseTransition(EnginePhase.RollingBack, elapsedMs);
        }
    }

    private async Task<Result<Unit>> ExecuteCoreAsync(PipelineContext ctx, CancellationToken ct)
    {
        await _uiChannel.SendAsync(
            new PipelineEvent.PhaseChanged(EnginePhase.RollingBack), ct);

        var loadResult = _journalStore.LoadAll();
        if (loadResult.IsFailure)
        {
            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Warning,
                    $"Rollback: could not load journal — {loadResult.Error.Message}"),
                ct);
            // Non-fatal: emit the event and continue; caller still gets RollingBack phase.
            return Unit.Value;
        }

        var entries = loadResult.Value;
        if (entries.Count == 0)
        {
            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Info, "Rollback: no journal entries to undo."), ct);
            return Unit.Value;
        }

        var executor = new RollbackExecutor(_undoOperations, _logger);
        var rollbackResult = await executor.ExecuteAsync(entries, ct);

        if (rollbackResult.IsFailure)
        {
            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Warning,
                    $"Rollback completed with errors: {rollbackResult.Error.Message}"),
                ct);
        }
        else
        {
            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Info,
                    $"Rollback complete: {entries.Count} operation(s) undone."),
                ct);
        }

        // Best-effort: clear journal even if rollback had partial failures
        var clearResult = _journalStore.Clear();
        if (clearResult.IsFailure)
        {
            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Warning,
                    $"Rollback: journal clear failed — {clearResult.Error.Message}"),
                ct);
        }

        // Emit per-step events from the journal
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.EntryType == JournalEntryType.SegmentBoundary)
                continue;

            await _uiChannel.SendAsync(
                new PipelineEvent.RollbackStep(new(
                    OperationKind: entry.EntryType.ToString(),
                    Target: entry.Description,
                    Succeeded: rollbackResult.IsSuccess,
                    Error: rollbackResult.IsFailure ? rollbackResult.Error : null)),
                ct);
        }

        return Unit.Value;
    }
}
