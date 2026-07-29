using System;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Declarative descriptor of a single <c>RadioButton</c> table row belonging to a
/// <c>RadioButtonGroup</c> control. ICE34 requires a <c>RadioButton</c> row for every
/// group option, keyed by the property the group control is bound to and the value it
/// assigns.
/// </summary>
/// <remarks>
/// <see cref="Property"/> is authored explicitly rather than inferred from the owning
/// control: inference across region placements is fragile, so the author states it
/// directly on each row instead.
/// </remarks>
internal sealed record DialogRadioButton
{
    private readonly string value = string.Empty;
    private readonly string property = string.Empty;

    /// <summary>The MSI property this radio button's owning group control is bound to.</summary>
    public required string Property
    {
        get => this.property;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Property must not be empty.", nameof(Property));
            }

            this.property = value;
        }
    }

    /// <summary>The value assigned to <see cref="Property"/> when this radio button is selected.</summary>
    public required string Value
    {
        get => this.value;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value must not be empty.", nameof(Value));
            }

            this.value = value;
        }
    }

    /// <summary>Display ordering among the group's radio buttons. Default is 1.</summary>
    public int Order { get; init; } = 1;

    /// <summary>Left edge in dialog units, relative to the owning group control.</summary>
    public int X { get; init; }

    /// <summary>Top edge in dialog units, relative to the owning group control.</summary>
    public int Y { get; init; }

    /// <summary>Width in dialog units.</summary>
    public int Width { get; init; }

    /// <summary>Height in dialog units.</summary>
    public int Height { get; init; }

    /// <summary>Label text, or a <c>!(loc.Key)</c> localization reference.</summary>
    public string? TextOrLocKey { get; init; }
}
