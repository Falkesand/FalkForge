using System;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Declarative descriptor of a single ControlCondition table row that toggles a
/// control's state (visibility, enablement, default) based on an MSI condition.
/// </summary>
/// <remarks>
/// Mirrors the three MSI ControlCondition table columns that vary per row (the
/// dialog name is contributed by the enclosing <see cref="DialogContent"/>): the
/// target control, the action to apply when the condition becomes true, and the
/// condition expression itself. Valid actions: <c>Enable</c>, <c>Disable</c>,
/// <c>Show</c>, <c>Hide</c>, <c>Default</c>.
/// </remarks>
public sealed record DialogControlCondition
{
    private readonly string control = string.Empty;
    private readonly string action = string.Empty;
    private readonly string condition = string.Empty;

    /// <summary>Name of the control affected by the condition.</summary>
    public required string Control
    {
        get => this.control;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Control must not be empty.", nameof(Control));
            }

            this.control = value;
        }
    }

    /// <summary>
    /// Action to apply when the condition evaluates true: <c>Enable</c>,
    /// <c>Disable</c>, <c>Show</c>, <c>Hide</c>, or <c>Default</c>.
    /// </summary>
    public required string Action
    {
        get => this.action;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Action must not be empty.", nameof(Action));
            }

            this.action = value;
        }
    }

    /// <summary>MSI condition expression that gates the action.</summary>
    public required string Condition
    {
        get => this.condition;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Condition must not be empty.", nameof(Condition));
            }

            this.condition = value;
        }
    }
}
