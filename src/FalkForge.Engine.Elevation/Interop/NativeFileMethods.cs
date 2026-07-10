using System.Runtime.InteropServices;

// Harden P/Invoke DLL resolution for this assembly: kernel32 is a System32 library, so
// resolve it only from the Windows system directory (prevents same-directory DLL hijacking).
// Mirrors the DefaultDllImportSearchPaths(System32) convention used in FalkForge.Platform.Windows
// and FalkForge.Engine.Protocol.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace FalkForge.Engine.Elevation.Interop;

using Microsoft.Win32.SafeHandles;

/// <summary>
/// kernel32 file bindings used for the handle-based no-follow write in the elevated process.
/// LibraryImport source-generation keeps this NativeAOT-safe.
/// </summary>
internal static partial class NativeFileMethods
{
    // Access rights
    internal const uint FileReadAttributes = 0x0080;
    internal const uint Delete = 0x00010000;
    internal const uint GenericWrite = 0x40000000;

    // Share modes (FILE_SHARE_DELETE deliberately absent where a handle must PIN the object
    // against rename/delete for its lifetime).
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;

    // Creation dispositions
    internal const uint OpenExisting = 3;
    internal const uint OpenAlways = 4;

    // Flags and attributes
    internal const uint FileFlagBackupSemantics = 0x02000000;
    internal const uint FileFlagOpenReparsePoint = 0x00200000;

    internal const uint FileAttributeReparsePoint = 0x00000400;
    internal const int ErrorAlreadyExists = 183;

    // FILE_INFO_BY_HANDLE_CLASS values
    internal const int FileAttributeTagInfoClass = 9; // FileAttributeTagInfo
    internal const int FileDispositionInfoClass = 4;  // FileDispositionInfo

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileAttributeTagInfo fileInformation,
        uint bufferSize);

    // The buffer is UTF-16 text; ushort keeps the parameter blittable so no assembly-wide
    // DisableRuntimeMarshalling is needed (callers reinterpret via MemoryMarshal.Cast).
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    internal static partial uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        ref ushort filePath,
        uint filePathLength,
        uint flags);

    // Converts 8.3 short components to their long form. The buffer is UTF-16 text; ushort
    // keeps the parameter blittable (see GetFinalPathNameByHandle above).
    [LibraryImport("kernel32.dll", EntryPoint = "GetLongPathNameW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetLongPathName(
        string shortPath,
        ref ushort longPath,
        uint longPathLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        in FileDispositionInfo fileInformation,
        uint bufferSize);

    /// <summary>Native FILE_ATTRIBUTE_TAG_INFO (FileAttributeTagInfo class).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileAttributeTagInfo
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    /// <summary>Native FILE_DISPOSITION_INFO (FileDispositionInfo class).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileDispositionInfo
    {
        internal byte DeleteFile; // BOOLEAN
    }
}
