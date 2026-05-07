namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;

/// <summary>
/// Apply phase step. Executes each <see cref="PlanAction"/> in the current plan
/// using <see cref="PackageExecutor"/>, journals each installed package for
/// potential rollback, and reports progress via <see cref="IUiChannel"/>.
/// </summary>
internal sealed class ApplyStep : IApplyStep
{
    private readonly PackageExecutor _executor;
    private readonly IRollbackJournalStore _journalStore;
    private readonly IUiChannel _uiChannel;

    public ApplyStep(
        PackageExecutor executor,
        IRollbackJournalStore journalStore,
        IUiChannel uiChannel)
    {
        _executor = executor;
        _journalStore = journalStore;
        _uiChannel = uiChannel;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        await _uiChannel.SendAsync(
            new PipelineEvent.PhaseChanged(EnginePhase.Applying), ct);

        if (ctx.Plan is null)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "ApplyStep: plan not populated — PlanStep must run first.");

        var actions = ctx.Plan.Actions;
        var total = actions.Count;

        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var action = actions[i];
            var percent = total > 0 ? (int)((double)i / total * 100) : 0;

            await _uiChannel.SendAsync(
                new PipelineEvent.Progress(percent,
                    $"{action.ActionType} {action.Package.DisplayName ?? action.PackageId}"),
                ct);

            // Build per-package progress relay that maps [0–100] to the slice
            // of overall progress this package occupies.
            var sliceStart = percent;
            var sliceEnd = total > 0 ? (int)((double)(i + 1) / total * 100) : 100;
            var uiChannel = _uiChannel;
            var packageProgress = new Progress<int>(p =>
            {
                var mapped = sliceStart + (int)((double)p / 100 * (sliceEnd - sliceStart));
                // Fire-and-forget; progress events are advisory
                _ = uiChannel.SendAsync(
                    new PipelineEvent.Progress(mapped, null),
                    CancellationToken.None);
            });

            var result = await _executor.ExecuteAsync(
                action, isDryRun: false, dryRunLogPath: null, ct, packageProgress);

            if (result.IsFailure)
                return Result<Unit>.Failure(result.Error);

            // Journal the successful install for rollback
            var journalEntry = BuildJournalEntry(action);
            if (journalEntry is not null)
            {
                var appendResult = _journalStore.Append(journalEntry);
                if (appendResult.IsFailure)
                    return Result<Unit>.Failure(appendResult.Error);
            }

            if (result.Value.Behavior
                    is ExitCodeBehavior.RebootRequired
                    or ExitCodeBehavior.ScheduleReboot)
            {
                ctx.RebootRequired = true;
            }

            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Info,
                    $"Completed {action.ActionType} of '{action.PackageId}'"),
                ct);
        }

        await _uiChannel.SendAsync(new PipelineEvent.Progress(100, null), ct);

        return Unit.Value;
    }

    private static JournalEntry? BuildJournalEntry(PlanAction action)
    {
        // Only journal installs — uninstall/repair don't need rollback entries
        if (action.ActionType != PlanActionType.Install)
            return null;

        var productCode = action.Properties.GetValueOrDefault("ProductCode")
                          ?? action.Package.Properties.GetValueOrDefault("ProductCode");

        if (productCode is not null)
        {
            return new JournalEntry
            {
                EntryType = JournalEntryType.MsiInstalled,
                Description = $"MSI installed: {action.Package.DisplayName ?? action.PackageId}",
                PackageId = action.PackageId,
                PackageType = action.Package.Type.ToString(),
                ProductCode = productCode
            };
        }

        return new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = $"Package installed: {action.Package.DisplayName ?? action.PackageId}",
            PackageId = action.PackageId,
            PackageType = action.Package.Type.ToString()
        };
    }
}
