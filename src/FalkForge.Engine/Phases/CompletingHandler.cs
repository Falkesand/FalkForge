namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Elevation;
using FalkForge.Engine.Protocol;

public sealed class CompletingHandler : IEnginePhaseHandler
{
    public EnginePhase Phase => EnginePhase.Completing;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // Tear down the elevated companion process
        await ElevationTeardown.TearDownAsync(context);

        // Cleanup: ensure exit code reflects success if no error was set
        if (context.ErrorMessage is null)
        {
            context.ExitCode = 0;
        }

        return EnginePhase.Shutdown;
    }
}
