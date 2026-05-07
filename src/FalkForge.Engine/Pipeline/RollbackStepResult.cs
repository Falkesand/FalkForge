namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Outcome of a single undo operation executed during rollback.
/// </summary>
public readonly record struct RollbackStepResult(
    string OperationKind,
    string Target,
    bool Succeeded,
    Error? Error);
