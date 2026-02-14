namespace FalkInstaller.Engine.Phases;

using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Messages;

public sealed class RollingBackHandler : IEnginePhaseHandler
{
    public EnginePhase Phase => EnginePhase.RollingBack;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // Rollback logic will use the RollbackJournal to undo operations.
        // For now, log the rollback attempt and transition to Shutdown.
        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new LogMessage
            {
                Text = "Rolling back changes...",
                Level = LogLevel.Warning
            }, ct);
        }

        // Ensure exit code reflects failure
        context.ExitCode = 1;

        return EnginePhase.Shutdown;
    }
}
