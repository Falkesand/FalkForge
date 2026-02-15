namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Protocol;

public sealed class ShutdownHandler : IEnginePhaseHandler
{
    public EnginePhase Phase => EnginePhase.Shutdown;

    public Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // The state machine loop exits when phase is Shutdown,
        // so this handler should not normally be called.
        return Task.FromResult(EnginePhase.Shutdown);
    }
}
