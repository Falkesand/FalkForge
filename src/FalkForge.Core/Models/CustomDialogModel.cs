namespace FalkForge.Models;

/// <summary>
/// A fully author-defined MSI dialog. Unlike the stock <see cref="MsiDialogSet"/> templates,
/// a custom dialog gives the author direct control over the dialog size, title, attributes,
/// and every control, event, condition, and tab-order link. The compiler translates each
/// custom dialog into MSI <c>Dialog</c> / <c>Control</c> / <c>ControlEvent</c> /
/// <c>ControlCondition</c> rows.
/// </summary>
/// <remarks>
/// Author custom dialogs via
/// <see cref="FalkForge.Builders.PackageBuilder.AddCustomDialog(string, System.Action{FalkForge.Builders.CustomDialogBuilder})"/>.
/// Custom dialogs are emitted in addition to any active stock <see cref="MsiDialogSet"/>, so an
/// author can either build a standalone custom wizard (set <see cref="SequenceNumber"/> to make
/// the dialog an install-UI entry point) or add extra dialogs reachable from a stock set via
/// <c>SpawnDialog</c> / <c>NewDialog</c> events.
/// </remarks>
public sealed record CustomDialogModel
{
    /// <summary>The dialog identifier, unique across the package. Maps to <c>Dialog.Dialog</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The window title, or <see langword="null"/> for an empty title. Maps to <c>Dialog.Title</c>.</summary>
    public string? Title { get; init; }

    /// <summary>Dialog width in dialog units. Defaults to the MSI standard 370.</summary>
    public int Width { get; init; } = 370;

    /// <summary>Dialog height in dialog units. Defaults to the MSI standard 270.</summary>
    public int Height { get; init; } = 270;

    /// <summary>Horizontal centering percentage (0–100). Maps to <c>Dialog.HCentering</c>.</summary>
    public int HCentering { get; init; } = 50;

    /// <summary>Vertical centering percentage (0–100). Maps to <c>Dialog.VCentering</c>.</summary>
    public int VCentering { get; init; } = 50;

    /// <summary>
    /// Raw <c>Dialog</c> table attribute bitmask. Defaults to <c>39</c>
    /// (<c>Visible | Modal | Minimize | TrackDiskSpace</c>).
    /// </summary>
    public int Attributes { get; init; } = 39;

    /// <summary>
    /// The control that receives focus first, or <see langword="null"/> to let the compiler
    /// pick the first authored control. Maps to <c>Dialog.Control_First</c>.
    /// </summary>
    public string? FirstControl { get; init; }

    /// <summary>The default (Enter) control, or <see langword="null"/>. Maps to <c>Dialog.Control_Default</c>.</summary>
    public string? DefaultControl { get; init; }

    /// <summary>The cancel (Escape) control, or <see langword="null"/>. Maps to <c>Dialog.Control_Cancel</c>.</summary>
    public string? CancelControl { get; init; }

    /// <summary>
    /// When set, the dialog is placed in the <c>InstallUISequence</c> table at this sequence
    /// number so it appears as an install-UI screen (the standard first-dialog slot is 1100).
    /// <see langword="null"/> (the default) means the dialog is only reachable from another
    /// dialog's <c>NewDialog</c> / <c>SpawnDialog</c> event.
    /// </summary>
    public int? SequenceNumber { get; init; }

    /// <summary>The controls placed on this dialog, in authoring order.</summary>
    public IReadOnlyList<CustomDialogControlModel> Controls { get; init; } = [];
}
