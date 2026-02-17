using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Interop;

[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    // FCI (File Compression Interface) - Cabinet creation API
    // All callbacks use Cdecl calling convention.
    // FCI uses ANSI strings, so we use DllImport with CharSet.Ansi.

    // ── Compression types (TCOMP) ──────────────────────────────────────

    internal const ushort TcompTypeNone = 0x0000;
    internal const ushort TcompTypeMszip = 0x0001;
    internal const ushort TcompTypeLzx = 0x0003;
    internal const int TcompLzxWindowLo = 15;
    internal const int TcompLzxWindowHi = 21;

    internal static ushort TcompLzxWindow(int windowSize) =>
        (ushort)(TcompTypeLzx | ((windowSize << 8) & 0x1F00));

    // ── Error reporting struct ──────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct ERF
    {
        public int erfOper;
        public int erfType;
        public int fError; // BOOL
    }

    // ── Cabinet parameters struct ───────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct CCAB
    {
        public uint cb;                // Maximum cabinet size (0 = no limit)
        public uint cbFolderThresh;    // Maximum folder size (0 = no limit)
        public uint cbReserveCFHeader; // Reserve space in header
        public uint cbReserveCFFolder; // Reserve space in folder
        public uint cbReserveCFData;   // Reserve space in data
        public int iCab;              // Cabinet index (1-based)
        public int iDisk;             // Disk index
        public int fFailOnIncompressible; // Fail on incompressible data

        public ushort setID;           // Cabinet set ID

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDisk;          // Disk name

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szCab;           // Cabinet file name

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szCabPath;       // Path for cabinet file (must end with backslash)
    }

    // ── Callback delegates (all Cdecl) ──────────────────────────────────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint FnFciAlloc(uint cb);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FnFciFree(nint memory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal delegate nint FnFciOpen(string pszFile, int oflag, int pmode, out int err, nint pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint FnFciRead(nint hf, nint memory, uint cb, out int err, nint pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint FnFciWrite(nint hf, nint memory, uint cb, out int err, nint pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int FnFciClose(nint hf, out int err, nint pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int FnFciSeek(nint hf, int dist, int seektype, out int err, nint pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal delegate int FnFciDelete(string pszFile, out int err, nint pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal delegate int FnFciFilePlaced(ref CCAB pccab, string pszFile, long cbFile, int fContinuation, nint pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int FnFciGetTempFile(nint pszTempName, int cbTempName, nint pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int FnFciGetNextCabinet(ref CCAB pccab, uint cbPrevCab, nint pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int FnFciStatus(uint typeStatus, uint cb1, uint cb2, nint pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal delegate nint FnFciGetOpenInfo(
        string pszName,
        out ushort pdate,
        out ushort ptime,
        out ushort pattribs,
        out int err,
        nint pv);

    // ── FCI functions ───────────────────────────────────────────────────

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    internal static extern nint FCICreate(
        ref ERF perf,
        FnFciFilePlaced pfnfcifp,
        FnFciAlloc pfna,
        FnFciFree pfnf,
        FnFciOpen pfnopen,
        FnFciRead pfnread,
        FnFciWrite pfnwrite,
        FnFciClose pfnclose,
        FnFciSeek pfnseek,
        FnFciDelete pfndelete,
        FnFciGetTempFile pfnfcigtf,
        ref CCAB pccab,
        nint pv);

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FCIAddFile(
        nint hfci,
        string pszSourceFile,
        string pszFileName,
        [MarshalAs(UnmanagedType.Bool)] bool fExecute,
        FnFciGetNextCabinet pfnfcignc,
        FnFciStatus pfnfcis,
        FnFciGetOpenInfo pfnfcigoi,
        ushort typeCompress);

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FCIFlushCabinet(
        nint hfci,
        [MarshalAs(UnmanagedType.Bool)] bool fGetNextCab,
        FnFciGetNextCabinet pfnfcignc,
        FnFciStatus pfnfcis);

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FCIDestroy(nint hfci);

    // ── FDI (File Decompression Interface) - Cabinet extraction API ────
    // All callbacks use Cdecl calling convention.
    // FDI uses ANSI strings, so we use DllImport with CharSet.Ansi.

    // ── FDI notification types ────────────────────────────────────────

    internal const int FdintCabinetInfo = 0;
    internal const int FdintPartialFile = 1;
    internal const int FdintCopyFile = 2;
    internal const int FdintCloseFileInfo = 3;
    internal const int FdintNextCabinet = 4;
    internal const int FdintEnumerate = 5;

    // ── FDI notification structure ────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct FdiNotification
    {
        public int cb;       // Uncompressed file size
        public nint psz1;   // File name (CopyFile), cabinet name (NextCabinet)
        public nint psz2;   // Disk name (NextCabinet)
        public nint psz3;   // Cabinet path (NextCabinet)
        public nint pv;     // User data pointer
        public nint hf;     // File handle
        public ushort date;  // DOS date
        public ushort time;  // DOS time
        public ushort attribs; // File attributes
        public ushort setID;   // Cabinet set ID
        public ushort iCabinet; // Cabinet number
        public ushort iFolder;  // Folder number
        public int fdie;    // FDI error code
    }

    // ── FDI callback delegates (all Cdecl) ────────────────────────────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal delegate nint FnFdiOpen(string pszFile, int oflag, int pmode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint FnFdiRead(nint hf, nint pv, uint cb);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint FnFdiWrite(nint hf, nint pv, uint cb);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int FnFdiClose(nint hf);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int FnFdiSeek(nint hf, int dist, int seektype);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint FnFdiNotify(int fdint, nint pfdin);

    // ── FDI functions ─────────────────────────────────────────────────

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern nint FDICreate(
        FnFciAlloc pfnAlloc,
        FnFciFree pfnFree,
        FnFdiOpen pfnOpen,
        FnFdiRead pfnRead,
        FnFdiWrite pfnWrite,
        FnFdiClose pfnClose,
        FnFdiSeek pfnSeek,
        int cpuType,
        ref ERF perf);

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FDICopy(
        nint hfdi,
        string pszCabinet,
        string pszCabPath,
        int flags,
        FnFdiNotify pfnfdin,
        nint pfnfdid,
        nint pvUser);

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FDIDestroy(nint hfdi);
}
