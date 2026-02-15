namespace FalkForge.Models;

public static class CustomActionType
{
    // Source types (bits 0-5)
    public const int DllFromBinary = 1;
    public const int ExeFromBinary = 2;
    public const int JScriptFromBinary = 5;
    public const int VBScriptFromBinary = 6;
    public const int ExeInDir = 34;
    public const int SetProperty = 51;
    public const int SetDirectory = 35;

    // Execution flags (bits 6+)
    public const int Continue = 0x040;
    public const int InScript = 0x100;
    public const int Rollback = 0x200;
    public const int Commit = 0x400;
    public const int NoImpersonate = 0x800;
}
