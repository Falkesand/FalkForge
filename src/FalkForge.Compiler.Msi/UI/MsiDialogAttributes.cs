namespace FalkForge.Compiler.Msi.UI;

/// <summary>
/// Bit flags for the MSI Dialog table Attributes column.
/// See: https://learn.microsoft.com/en-us/windows/win32/msi/dialog-table
/// </summary>
[Flags]
internal enum MsiDialogAttributes
{
    None = 0,
    Visible = 0x00000001,
    Modal = 0x00000002,
    Minimize = 0x00000004,
    SysModal = 0x00000008,
    KeepModeless = 0x00000010,
    TrackDiskSpace = 0x00000020,
    UseCustomPalette = 0x00000040,
    RightToLeftReadingOrder = 0x00000080,
    RightAligned = 0x00000100,
    LeftScroll = 0x00000200,
    BiDi = 0x00000400,
    Error = 0x00000800,
}
