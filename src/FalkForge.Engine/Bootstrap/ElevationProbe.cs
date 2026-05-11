using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Engine.Bootstrap;

/// <summary>
/// Probes whether the current process token has UAC elevation (administrative privilege).
/// Uses P/Invoke against advapi32.dll / kernel32.dll — NativeAOT-safe, no reflection.
/// </summary>
/// <remarks>
/// Assembly-level <c>[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]</c> is
/// already declared in NativeRestartManagerMethods.cs and covers the entire Engine assembly,
/// so no duplicate attribute is needed here.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static partial class ElevationProbe
{
    // TOKEN_QUERY access right — sufficient to call GetTokenInformation.
    private const uint TokenQuery = 0x0008;

    // TokenElevation information class (value 20) — returns TOKEN_ELEVATION struct.
    private const uint TokenElevationClass = 20;

    /// <summary>
    /// Returns <see langword="true"/> when the current process token is elevated (UAC admin);
    /// <see langword="false"/> otherwise, including on any P/Invoke failure (fail-safe).
    /// </summary>
    internal static bool IsElevated()
    {
        nint hProcess = GetCurrentProcess();

        if (!OpenProcessToken(hProcess, TokenQuery, out nint hToken))
            return false;

        try
        {
            // TOKEN_ELEVATION is a single DWORD: non-zero means elevated.
            uint elevation = 0;
            bool ok = GetTokenInformation(
                hToken,
                TokenElevationClass,
                ref elevation,
                sizeof(uint),
                out _);

            return ok && elevation != 0;
        }
        finally
        {
            CloseHandle(hToken);
        }
    }

    // ── P/Invoke declarations ────────────────────────────────────────────────

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(
        nint processHandle,
        uint desiredAccess,
        out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        nint tokenHandle,
        uint tokenInformationClass,
        ref uint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
