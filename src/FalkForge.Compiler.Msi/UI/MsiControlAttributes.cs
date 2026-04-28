namespace FalkForge.Compiler.Msi.UI;

/// <summary>
/// Bit flags for the MSI Control table Attributes column.
/// See: https://learn.microsoft.com/en-us/windows/win32/msi/control-table
/// </summary>
/// <remarks>
/// Bits 16 and above are control-type-specific. Several control types reuse the same
/// bit positions with different semantics. The enum members document which control
/// type each flag applies to. Callers must combine only flags valid for the target
/// control type.
/// </remarks>
[Flags]
internal enum MsiControlAttributes
{
    None = 0,
    Visible = 0x00000001,
    Enabled = 0x00000002,
    Sunken = 0x00000004,
    Indirect = 0x00000008,
    Integer = 0x00000010,
    RightToLeft = 0x00000020,
    RightAligned = 0x00000040,
    LeftScroll = 0x00000080,

    // Bit 16+: control-type-specific flags.
    // Text control
    Transparent = 0x00010000,
    NoPrefix = 0x00020000,

    // ProgressBar control (same bit as Transparent, different meaning)
    Progress95 = 0x00010000,

    // VolumeCostList control (same bits, different meanings)
    RemovableMedia = 0x00010000,
    FixedMedia = 0x00020000,
    RemoteMedia = 0x00040000,

    // Icon control
    FixedSize = 0x00100000,
}
