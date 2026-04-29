using System;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Declarative descriptor of a single EventMapping table row that subscribes a
/// dialog control to an MSI runtime event such as <c>SetProgress</c> or
/// <c>ActionText</c>.
/// </summary>
/// <remarks>
/// Mirrors the three MSI EventMapping table columns that vary per row (the dialog
/// name is contributed by the enclosing <see cref="DialogContent"/>): the control
/// receiving updates, the event name fired by the engine, and the control attribute
/// to update.
/// </remarks>
public sealed record DialogEventMapping
{
    private readonly string control = string.Empty;
    private readonly string @event = string.Empty;
    private readonly string attribute = string.Empty;

    /// <summary>Name of the control that receives event-driven updates.</summary>
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

    /// <summary>MSI event name, e.g. <c>SetProgress</c>, <c>ActionText</c>, <c>ActionData</c>.</summary>
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

    /// <summary>Control attribute to update when the event fires, e.g. <c>Progress</c> or <c>Text</c>.</summary>
    public required string Attribute
    {
        get => this.attribute;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Attribute must not be empty.", nameof(Attribute));
            }

            this.attribute = value;
        }
    }
}
