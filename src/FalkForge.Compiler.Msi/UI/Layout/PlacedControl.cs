using System;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// A single MSI control placed inside a <see cref="RegionPlacement"/>.
/// </summary>
/// <remarks>
/// Coordinates are intentionally optional: the owning region's <see cref="RegionPolicy"/>
/// is responsible for laying controls out. <see cref="OverrideX"/>, <see cref="OverrideY"/>,
/// <see cref="OverrideWidth"/>, and <see cref="OverrideHeight"/> are honored by the
/// <see cref="RegionPolicy.Absolute"/> policy and ignored by the packed policies. The
/// declarative shape is decoupled from canvas geometry so that the same shape can
/// target different layouts (phase 5+).
/// </remarks>
public sealed record PlacedControl
{
    private readonly string name = string.Empty;
    private readonly string type = string.Empty;

    /// <summary>Control identifier; must match the C-style identifier pattern.</summary>
    public required string Name
    {
        get => this.name;
        init
        {
            DialogRegion.ValidateIdentifier(value, nameof(Name));
            this.name = value;
        }
    }

    /// <summary>MSI control type ("PushButton", "Text", "Edit", "Bitmap", etc.). Must be non-empty.</summary>
    public required string Type
    {
        get => this.type;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Control type must be non-empty.", nameof(Type));
            }

            this.type = value;
        }
    }

    /// <summary>Display text or <c>!(loc.X)</c> localization key.</summary>
    public string TextOrLocKey { get; init; } = string.Empty;

    /// <summary>Bound MSI property name, or null if the control is not bound.</summary>
    public string? Property { get; init; }

    /// <summary>Override X offset for <see cref="RegionPolicy.Absolute"/> regions; null defers to the policy.</summary>
    public int? OverrideX { get; init; }

    /// <summary>Override Y offset for <see cref="RegionPolicy.Absolute"/> regions; null defers to the policy.</summary>
    public int? OverrideY { get; init; }

    /// <summary>Override width; null defers to the policy / region defaults.</summary>
    public int? OverrideWidth { get; init; }

    /// <summary>Override height; null defers to the policy / region defaults.</summary>
    public int? OverrideHeight { get; init; }
}
