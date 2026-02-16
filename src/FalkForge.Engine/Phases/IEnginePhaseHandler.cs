namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Protocol;

public interface IEnginePhaseHandler
{
    EnginePhase Phase { get; }
    Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct);
}
