namespace FalkForge.Engine;

using FalkForge.Engine.Pipeline;

/// <summary>Terminal state of a completed engine session.</summary>
public enum EngineTerminalState
{
    /// <summary>All phases ran to completion without error.</summary>
    Completed,

    /// <summary>Session ended via user Cancel/Shutdown or <see cref="CancellationToken"/> cancellation.</summary>
    Cancelled,

    /// <summary>Apply phase failed and the rollback step executed.</summary>
    RolledBack,

    /// <summary>A phase returned an unrecoverable failure.</summary>
    Failed
}

/// <summary>
/// Summary of rollback execution: how many undo operations ran and how many failed.
/// </summary>
public readonly record struct RollbackSummary(
    int StepsExecuted,
    int StepsFailed,
    IReadOnlyList<RollbackStepResult> Steps);
