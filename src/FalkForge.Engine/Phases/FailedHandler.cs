namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Messages;

public sealed class FailedHandler : IEnginePhaseHandler
{
    public EnginePhase Phase => EnginePhase.Failed;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        context.ExitCode = 1;

        // Notify UI of the error
        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new ErrorMessage
            {
                Message = context.ErrorMessage ?? "Unknown error",
                Kind = ErrorKind.EngineError
            }, ct);
        }

        // If there's a plan that was partially executed, attempt rollback
        if (context.CurrentPlan is not null)
        {
            return EnginePhase.RollingBack;
        }

        return EnginePhase.Shutdown;
    }
}
