namespace FalkForge.Models;

/// <summary>
/// The action applied to a control when its MSI condition evaluates true, written to the
/// <c>ControlCondition</c> table <c>Action</c> column. Member names match the exact string
/// Windows Installer expects.
/// </summary>
public enum CustomConditionAction
{
    /// <summary>Restore the control's default (initial) state when the condition is true.</summary>
    Default,

    /// <summary>Disable the control (greyed, non-interactive) when the condition is true.</summary>
    Disable,

    /// <summary>Enable the control when the condition is true.</summary>
    Enable,

    /// <summary>Hide the control when the condition is true.</summary>
    Hide,

    /// <summary>Show the control when the condition is true.</summary>
    Show,
}
