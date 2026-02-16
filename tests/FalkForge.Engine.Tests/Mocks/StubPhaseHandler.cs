namespace FalkForge.Engine.Tests.Mocks;

using FalkForge.Engine.Phases;
using FalkForge.Engine.Protocol;

public sealed class StubPhaseHandler : IEnginePhaseHandler
{
    private readonly Func<EngineContext, CancellationToken, Task<EnginePhase>> _execute;

    public StubPhaseHandler(EnginePhase phase, EnginePhase transitionTo)
    {
        Phase = phase;
        _execute = (_, _) => Task.FromResult(transitionTo);
    }

    public StubPhaseHandler(EnginePhase phase, Func<EngineContext, CancellationToken, Task<EnginePhase>> execute)
    {
        Phase = phase;
        _execute = execute;
    }

    public EnginePhase Phase { get; }

    public Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        return _execute(context, ct);
    }
}
