using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Interop;

/// <summary>
///     Shared FCI/FDI callback logic that is byte-identical between <see cref="Msi.CabinetBuilder"/>
///     (cabinet creation) and <see cref="Msi.CabinetExtractor"/> (cabinet extraction). Only the
///     genuinely duplicated members live here — the open/read/write/close/seek callbacks differ
///     between the two sides (extra FCI <c>out int err, nint pv</c> parameters, distinct error
///     channels, FileShare modes, and handle counters) and must NOT be merged.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CabinetCallbackShim
{
    internal static nint Alloc(uint cb)
    {
        return Marshal.AllocHGlobal((int)cb);
    }

    internal static void Free(nint memory)
    {
        Marshal.FreeHGlobal(memory);
    }

    // ── C-style open flag mapping ───────────────────────────────────────

    internal static (FileMode mode, FileAccess access) MapOpenFlags(int oflag)
    {
        // C runtime flags: _O_RDONLY=0, _O_WRONLY=1, _O_RDWR=2
        // _O_CREAT=0x100, _O_TRUNC=0x200, _O_BINARY=0x8000
        const int oRdonly = 0x0000;
        const int oWronly = 0x0001;
        const int oRdwr = 0x0002;
        const int oCreat = 0x0100;
        const int oTrunc = 0x0200;

        var accessMode = oflag & 0x0003;
        var access = accessMode switch
        {
            oRdonly => FileAccess.Read,
            oWronly => FileAccess.Write,
            oRdwr => FileAccess.ReadWrite,
            _ => FileAccess.ReadWrite
        };

        FileMode mode;
        if ((oflag & oCreat) != 0 && (oflag & oTrunc) != 0)
            mode = FileMode.Create;
        else if ((oflag & oCreat) != 0)
            mode = FileMode.OpenOrCreate;
        else if ((oflag & oTrunc) != 0)
            mode = FileMode.Truncate;
        else
            mode = FileMode.Open;

        return (mode, access);
    }
}
