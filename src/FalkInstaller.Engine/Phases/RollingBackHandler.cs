namespace FalkInstaller.Engine.Phases;

using FalkInstaller.Engine.Journal;
using FalkInstaller.Engine.Planning;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Messages;

public sealed class RollingBackHandler : IEnginePhaseHandler
{
    private readonly RollbackExecutor _rollbackExecutor;
    private readonly RollbackJournal? _journal;

    public RollingBackHandler(RollbackExecutor rollbackExecutor, RollbackJournal? journal = null)
    {
        _rollbackExecutor = rollbackExecutor;
        _journal = journal;
    }

    public EnginePhase Phase => EnginePhase.RollingBack;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        var journal = _journal ?? context.Journal;
        var plan = context.CurrentPlan;
        var failedSegmentIndex = context.FailedSegmentIndex;

        // Determine rollback scope based on segment information
        string rollbackScope;
        if (plan is not null && failedSegmentIndex >= 0 && failedSegmentIndex < plan.Segments.Count)
        {
            var segment = plan.Segments[failedSegmentIndex];
            rollbackScope = $"segment '{segment.BoundaryId}' ({segment.Actions.Count} action(s))";
        }
        else
        {
            rollbackScope = "all changes";
        }

        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new LogMessage
            {
                Text = $"Rolling back {rollbackScope}...",
                Level = LogLevel.Warning
            }, ct);
        }

        // Execute rollback using the RollbackExecutor
        Result<Unit> rollbackResult;
        if (journal is not null && plan is not null && failedSegmentIndex >= 0 && failedSegmentIndex < plan.Segments.Count)
        {
            var segment = plan.Segments[failedSegmentIndex];

            if (context.UiPipe is not null && context.UiPipe.IsConnected)
            {
                await context.UiPipe.SendAsync(new LogMessage
                {
                    Text = $"Segment '{segment.BoundaryId}': rolling back {segment.Actions.Count} action(s), vital={segment.Vital}",
                    Level = LogLevel.Info
                }, ct);
            }

            // Roll back only the failed segment's journal entries
            rollbackResult = await _rollbackExecutor.ExecuteSegmentAsync(journal, segment.BoundaryId, ct);
        }
        else if (journal is not null)
        {
            // No segment info: roll back all non-boundary entries
            var allEntries = journal.Entries
                .Where(e => e.EntryType != JournalEntryType.SegmentBoundary)
                .ToList();
            rollbackResult = await _rollbackExecutor.ExecuteAsync(allEntries, ct);
        }
        else
        {
            // No journal available: log and continue with existing behavior
            rollbackResult = Unit.Value;

            if (plan is not null && failedSegmentIndex >= 0 && failedSegmentIndex < plan.Segments.Count)
            {
                var segment = plan.Segments[failedSegmentIndex];

                if (context.UiPipe is not null && context.UiPipe.IsConnected)
                {
                    await context.UiPipe.SendAsync(new LogMessage
                    {
                        Text = $"Segment '{segment.BoundaryId}': no journal available, rollback skipped",
                        Level = LogLevel.Warning
                    }, ct);
                }
            }
        }

        // Log rollback result
        if (rollbackResult.IsFailure && context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new LogMessage
            {
                Text = $"Rollback errors: {rollbackResult.Error.Message}",
                Level = LogLevel.Error
            }, ct);
        }

        // Handle vital vs non-vital segment transitions
        if (plan is not null && failedSegmentIndex >= 0 && failedSegmentIndex < plan.Segments.Count)
        {
            var segment = plan.Segments[failedSegmentIndex];

            if (segment.Vital)
            {
                if (context.UiPipe is not null && context.UiPipe.IsConnected)
                {
                    await context.UiPipe.SendAsync(new LogMessage
                    {
                        Text = "Vital rollback boundary: install will fail if rollback fails",
                        Level = LogLevel.Warning
                    }, ct);
                }
            }
            else
            {
                if (context.UiPipe is not null && context.UiPipe.IsConnected)
                {
                    await context.UiPipe.SendAsync(new LogMessage
                    {
                        Text = "Non-vital rollback boundary: continuing despite rollback",
                        Level = LogLevel.Warning
                    }, ct);
                }
            }
        }

        // Non-vital segments allow the installer to continue completing
        if (plan is not null && failedSegmentIndex >= 0 && failedSegmentIndex < plan.Segments.Count
            && !plan.Segments[failedSegmentIndex].Vital)
        {
            return EnginePhase.Completing;
        }

        // Vital segment or unknown: fail the entire install
        context.ExitCode = 1;

        return EnginePhase.Shutdown;
    }
}
