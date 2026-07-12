namespace FalkForge.Models;

/// <summary>
/// A single control placed on a <see cref="CustomDialogModel"/>. Maps directly to one MSI
/// <c>Control</c> table row, plus any <c>ControlEvent</c> / <c>ControlCondition</c> rows
/// contributed via <see cref="Events"/> and <see cref="Conditions"/>.
/// </summary>
/// <remarks>
/// Coordinates and sizes are in MSI dialog units (DLU). <see cref="Attributes"/> is the raw
/// <c>Control</c> table <c>Attributes</c> bitmask; the default <c>3</c> is
/// <c>msidbControlAttributesVisible | msidbControlAttributesEnabled</c>.
/// </remarks>
public sealed record CustomDialogControlModel
{
    /// <summary>The control identifier, unique within its dialog. Maps to <c>Control.Control</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The control type. Maps to <c>Control.Type</c>.</summary>
    public required CustomControlType Type { get; init; }

    /// <summary>Left edge in dialog units. Maps to <c>Control.X</c>.</summary>
    public int X { get; init; }

    /// <summary>Top edge in dialog units. Maps to <c>Control.Y</c>.</summary>
    public int Y { get; init; }

    /// <summary>Width in dialog units. Maps to <c>Control.Width</c>.</summary>
    public int Width { get; init; }

    /// <summary>Height in dialog units. Maps to <c>Control.Height</c>.</summary>
    public int Height { get; init; }

    /// <summary>
    /// Raw <c>Control</c> table attribute bitmask. Defaults to <c>3</c>
    /// (<c>Visible | Enabled</c>).
    /// </summary>
    public int Attributes { get; init; } = 3;

    /// <summary>
    /// The MSI property this control is bound to, or <see langword="null"/> for controls that
    /// carry no data (for example <see cref="CustomControlType.Text"/> or
    /// <see cref="CustomControlType.PushButton"/>). Maps to <c>Control.Property</c>.
    /// </summary>
    public string? Property { get; init; }

    /// <summary>
    /// The control text, a <c>!(loc.Key)</c> localization reference, or (for
    /// <see cref="CustomControlType.Bitmap"/> / <see cref="CustomControlType.Icon"/>) the name
    /// of an embedded <c>Binary</c> stream. Maps to <c>Control.Text</c>.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// The name of the control that receives focus next in tab order, or
    /// <see langword="null"/> to end the tab cycle. Maps to <c>Control.Control_Next</c>.
    /// Must reference a control in the same dialog.
    /// </summary>
    public string? NextControl { get; init; }

    /// <summary>The <c>ControlEvent</c> rows fired by this control.</summary>
    public IReadOnlyList<CustomDialogControlEventModel> Events { get; init; } = [];

    /// <summary>The <c>ControlCondition</c> rows that toggle this control's state.</summary>
    public IReadOnlyList<CustomDialogControlConditionModel> Conditions { get; init; } = [];
}
