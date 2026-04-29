using System;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Declarative descriptor of a single ControlEvent table row firing from a dialog
/// control. The composer translates these into runtime <c>MsiControlEventModel</c>
/// instances when materializing a <see cref="MsiDialogModel"/>.
/// </summary>
/// <remarks>
/// The descriptor mirrors the four MSI ControlEvent table columns that vary per row
/// (the dialog name is contributed by the enclosing <see cref="DialogContent"/>):
/// the firing control, the event verb, an event-specific argument, and an optional
/// MSI condition. <see cref="Order"/> orders rows that share both control and event.
/// </remarks>
public sealed record DialogControlEvent
{
    private readonly string control = string.Empty;
    private readonly string @event = string.Empty;

    /// <summary>Name of the control whose press fires this event.</summary>
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
    /// MSI event verb. Standard values include <c>NewDialog</c>, <c>EndDialog</c>,
    /// <c>SpawnDialog</c>, <c>DoAction</c>, <c>Reset</c>; <c>[PropertyName]</c> assigns
    /// the property to the value carried in <see cref="Argument"/>.
    /// </summary>
    public required string Event
    {
        get => this.@event;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Event must not be empty.", nameof(Event));
            }

            this.@event = value;
        }
    }

    /// <summary>
    /// Event-specific argument: target dialog name, exit code, action name, property
    /// value, etc. Empty when the event takes no argument.
    /// </summary>
    public string Argument { get; init; } = string.Empty;

    /// <summary>
    /// MSI condition that gates the event. <c>null</c> means "always fire" and the
    /// composer emits the canonical "1" condition string.
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// Fire ordering when multiple rows share the same control and event verb.
    /// Lower numbers fire first; default is 1.
    /// </summary>
    public int Order { get; init; } = 1;
}
