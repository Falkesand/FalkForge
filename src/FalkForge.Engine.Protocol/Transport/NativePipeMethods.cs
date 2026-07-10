using System.Runtime.InteropServices;

// Harden P/Invoke DLL resolution for this assembly: kernel32 is a System32 library, so
// resolve it only from the Windows system directory (prevents same-directory DLL hijacking).
// Mirrors the DefaultDllImportSearchPaths(System32) convention used in FalkForge.Platform.Windows.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace FalkForge.Engine.Protocol.Transport;

using Microsoft.Win32.SafeHandles;

/// <summary>
/// kernel32 bindings used for server-PID binding on the elevated pipe client.
/// LibraryImport source-generation keeps this NativeAOT-safe.
/// </summary>
internal static partial class NativePipeMethods
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeServerProcessId(SafePipeHandle Pipe, out uint ServerProcessId);
}
