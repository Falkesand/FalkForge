namespace FalkForge.Engine;

using FalkForge.Engine.Pipeline;

/// <summary>
/// Terminal result of an <see cref="EngineSession"/> run. Captures the final state,
/// any error, rollback summary, wall-clock duration, and paths to log files produced
/// during the session.
/// </summary>
public readonly record struct EngineOutcome(
    EngineTerminalState State,
    Error? Error,
    RollbackSummary? Rollback,
    TimeSpan Duration,
    IReadOnlyList<string> LogFiles);
