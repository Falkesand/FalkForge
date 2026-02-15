namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Protocol;

public sealed class CompletingHandler : IEnginePhaseHandler
{
    public EnginePhase Phase => EnginePhase.Completing;

    public Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // Cleanup: ensure exit code reflects success if no error was set
        if (context.ErrorMessage is null)
        {
            context.ExitCode = 0;
        }

        return Task.FromResult(EnginePhase.Shutdown);
    }
}
