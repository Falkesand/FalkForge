namespace FalkInstaller.Engine.Phases;

using FalkInstaller.Engine.Planning;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Messages;

public sealed class RollingBackHandler : IEnginePhaseHandler
{
    public EnginePhase Phase => EnginePhase.RollingBack;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
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

        // When segments are available, only actions in the failed segment are rolled back.
        // Actions in previously completed segments remain installed.
        if (plan is not null && failedSegmentIndex >= 0 && failedSegmentIndex < plan.Segments.Count)
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

            // In a full implementation, we would reverse-execute the actions in this segment.
            // For now, log the intent and track the vital flag for transition decision.
            if (segment.Vital)
            {
                // Vital segment: failure to roll back means entire install fails
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
                // Non-vital segment: log warning and allow continuation
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
