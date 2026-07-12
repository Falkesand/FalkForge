namespace FalkForge.Models;

/// <summary>
/// A single <c>ControlCondition</c> table row that toggles a control's state
/// (show/hide/enable/disable/default) when its MSI condition evaluates true. The owning
/// control name and dialog name are supplied by the enclosing
/// <see cref="CustomDialogControlModel"/> / <see cref="CustomDialogModel"/> at compile time.
/// </summary>
public sealed record CustomDialogControlConditionModel
{
    /// <summary>The action to apply to the control when <see cref="Condition"/> is true.</summary>
    public required CustomConditionAction Action { get; init; }

    /// <summary>The MSI condition expression that gates the action. Must be non-empty.</summary>
    public required string Condition { get; init; }
}
