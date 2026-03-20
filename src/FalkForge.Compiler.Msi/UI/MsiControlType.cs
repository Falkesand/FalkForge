namespace FalkForge.Compiler.Msi.UI;

/// <summary>
/// Standard MSI control types for the Control table Type column.
/// Member names match the exact strings expected by Windows Installer.
/// </summary>
internal enum MsiControlType
{
    Text,
    PushButton,
    Line,
    CheckBox,
    ScrollableText,
    PathEdit,
    SelectionTree,
    VolumeCostList,
    ProgressBar,
    Bitmap,
    RadioButtonGroup,
    ComboBox,
    Edit,
    ListBox,
    DirectoryCombo,
    DirectoryList,
    MaskedEdit,
    Icon,
    GroupBox,
}
