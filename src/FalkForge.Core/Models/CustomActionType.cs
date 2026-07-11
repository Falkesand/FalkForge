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

    // Execution flags (bits 6+). Values match the Windows Installer CustomAction Type bit layout
    // (msi.h / MSDN "Custom Action In-Script Execution Options"). NOTE: 0x100 and 0x200 are the
    // Rollback / Commit bits only when InScript (0x400) is ALSO set; without InScript the same bits
    // mean FirstSequence / OncePerProcess. The builders always combine Rollback/Commit with InScript,
    // so a deferred rollback action is 0x400|0x100 and a commit action is 0x400|0x200.
    public const int Continue = 0x040;
    public const int InScript = 0x400;
    public const int Rollback = 0x100;
    public const int Commit = 0x200;
    public const int NoImpersonate = 0x800;
}