namespace FalkInstaller.Engine.Phases;

using FalkInstaller.Engine.Protocol;

public interface IEnginePhaseHandler
{
    EnginePhase Phase { get; }
    Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct);
}
