namespace FalkForge.Compiler.Msi.UI;

/// <summary>
/// Actions for the MSI ControlCondition table Action column.
/// Member names match the exact strings expected by Windows Installer.
/// </summary>
internal enum MsiConditionAction
{
    Disable,
    Enable,
    Hide,
    Show,
    Default,
}
