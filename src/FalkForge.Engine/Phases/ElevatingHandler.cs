namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Protocol;

public sealed class ElevatingHandler : IEnginePhaseHandler
{
    public EnginePhase Phase => EnginePhase.Elevating;

    public Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // Placeholder: elevation logic will be implemented with the elevation subsystem.
        // For now, transition directly to Applying.
        return Task.FromResult(EnginePhase.Applying);
    }
}
