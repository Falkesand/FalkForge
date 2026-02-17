namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Elevation;
using FalkForge.Engine.Protocol;

public sealed class ShutdownHandler : IEnginePhaseHandler
{
    public EnginePhase Phase => EnginePhase.Shutdown;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // Safety net: tear down elevation if CompletingHandler did not run
        // (e.g., direct transition to Shutdown from an error path).
        await ElevationTeardown.TearDownAsync(context);

        return EnginePhase.Shutdown;
    }
}
