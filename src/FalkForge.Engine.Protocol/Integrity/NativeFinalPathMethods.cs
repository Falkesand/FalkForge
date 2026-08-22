namespace FalkForge.Engine.Protocol.Integrity;

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// kernel32 binding used to ask Windows which file an already-open handle actually refers to.
/// <see cref="LibraryImportAttribute"/> source-generation keeps this NativeAOT-safe and makes the
/// marshalling of every parameter a compile-time check.
/// </summary>
/// <remarks>
/// The assembly-level <c>DefaultDllImportSearchPaths(System32)</c> declared in
/// <c>Transport/NativePipeMethods.cs</c> covers this import too, so kernel32 resolves only from
/// the Windows system directory.
/// </remarks>
internal static partial class NativeFinalPathMethods
{
    /// <summary>
    /// Returns the length in characters of the path written to <paramref name="lpszFilePath"/>,
    /// not counting the terminating null, or the required length including the terminating null
    /// when the buffer was too small, or 0 on failure.
    /// </summary>
    /// <remarks>
    /// <c>StringMarshalling.Utf16</c> is what makes <see cref="char"/> blittable to the source
    /// generator. Without it the generator emits SYSLIB1051, because under runtime marshalling a
    /// <see cref="char"/> may be narrowed to ANSI.
    /// </remarks>
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        ref char lpszFilePath,
        uint cchFilePath,
        uint dwFlags);
}
