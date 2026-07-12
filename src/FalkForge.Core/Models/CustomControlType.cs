namespace FalkForge.Models;

/// <summary>
/// The standard Windows Installer control types that can be authored on a
/// <see cref="CustomDialogModel"/>. Member names match the exact string the MSI
/// <c>Control</c> table <c>Type</c> column expects, so the compiler can map a value
/// to its table representation without a lookup table.
/// </summary>
/// <remarks>
/// See <see href="https://learn.microsoft.com/en-us/windows/win32/msi/controls">MSI controls</see>.
/// Control types that require a companion table (for example <c>RadioButtonGroup</c> needs a
/// <c>RadioButton</c> table, <c>ComboBox</c> needs a <c>ComboBox</c> table) can still be
/// authored so their <c>Control</c> row is emitted, but the companion option rows must be
/// supplied separately (for example via a custom table). See the authoring documentation.
/// </remarks>
public enum CustomControlType
{
    /// <summary>Static, non-interactive text.</summary>
    Text,

    /// <summary>A clickable push button. Wire navigation with control events.</summary>
    PushButton,

    /// <summary>A horizontal etched line used as a visual separator.</summary>
    Line,

    /// <summary>A check box bound to an MSI property.</summary>
    CheckBox,

    /// <summary>A scrollable multi-line read-only text area (typically for a licence).</summary>
    ScrollableText,

    /// <summary>An editable path field bound to a directory property.</summary>
    PathEdit,

    /// <summary>A feature-selection tree.</summary>
    SelectionTree,

    /// <summary>A list of volumes with disk-cost columns.</summary>
    VolumeCostList,

    /// <summary>A progress bar driven by <c>SetProgress</c> events.</summary>
    ProgressBar,

    /// <summary>A bitmap image. The control text names an embedded <c>Binary</c> stream.</summary>
    Bitmap,

    /// <summary>A group of radio buttons bound to an MSI property (needs a <c>RadioButton</c> table).</summary>
    RadioButtonGroup,

    /// <summary>A drop-down combo box (needs a <c>ComboBox</c> table).</summary>
    ComboBox,

    /// <summary>A single-line editable text field bound to an MSI property.</summary>
    Edit,

    /// <summary>A list box (needs a <c>ListBox</c> table).</summary>
    ListBox,

    /// <summary>A drop-down of available drives.</summary>
    DirectoryCombo,

    /// <summary>A list of sub-directories under the current directory.</summary>
    DirectoryList,

    /// <summary>An edit field with an input mask.</summary>
    MaskedEdit,

    /// <summary>An icon. The control text names an embedded <c>Binary</c> stream.</summary>
    Icon,

    /// <summary>A labelled group box frame.</summary>
    GroupBox,
}
