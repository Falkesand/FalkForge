namespace FalkForge.Engine;

using FalkForge.Engine.Phases;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Messages;

public sealed class EngineStateMachine
{
    private readonly Dictionary<EnginePhase, IEnginePhaseHandler> _handlers;
    private EnginePhase _currentPhase = EnginePhase.Initializing;

    public EnginePhase CurrentPhase => _currentPhase;

    public EngineStateMachine(IEnumerable<IEnginePhaseHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Phase);
    }

    public async Task<int> RunAsync(EngineContext context, CancellationToken ct)
    {
        while (_currentPhase != EnginePhase.Shutdown)
        {
            if (!_handlers.TryGetValue(_currentPhase, out var handler))
            {
                context.ErrorMessage = $"No handler for phase: {_currentPhase}";
                _currentPhase = EnginePhase.Failed;
                continue;
            }

            var nextPhase = await handler.ExecuteAsync(context, ct);

            if (!IsValidTransition(_currentPhase, nextPhase))
            {
                context.ErrorMessage = $"Invalid transition: {_currentPhase} -> {nextPhase}";
                _currentPhase = EnginePhase.Failed;
                continue;
            }

            _currentPhase = nextPhase;

            // Notify UI of phase change
            if (context.UiPipe is not null && context.UiPipe.IsConnected)
            {
                await context.UiPipe.SendAsync(new PhaseChangedMessage { Phase = _currentPhase }, ct);
            }
        }

        return context.ExitCode;
    }

    public static bool IsValidTransition(EnginePhase from, EnginePhase to)
    {
        // Any phase can go to Failed
        if (to == EnginePhase.Failed) return true;
        // Failed can go to RollingBack or Shutdown
        if (from == EnginePhase.Failed) return to is EnginePhase.RollingBack or EnginePhase.Shutdown;
        // RollingBack -> Shutdown
        if (from == EnginePhase.RollingBack) return to == EnginePhase.Shutdown;

        return (from, to) switch
        {
            (EnginePhase.Initializing, EnginePhase.Detecting) => true,
            (EnginePhase.Detecting, EnginePhase.Planning) => true,
            (EnginePhase.Planning, EnginePhase.Elevating) => true,
            (EnginePhase.Planning, EnginePhase.Applying) => true, // PerUser doesn't need elevation
            (EnginePhase.Elevating, EnginePhase.Applying) => true,
            (EnginePhase.Applying, EnginePhase.RollingBack) => true, // User cancellation during apply
            (EnginePhase.Applying, EnginePhase.Completing) => true,
            (EnginePhase.Completing, EnginePhase.Shutdown) => true,
            _ => false
        };
    }
}
