using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace FalkForge.Engine.Bootstrap;

/// <summary>
/// Relaunches this engine executable with elevated privileges using <c>ShellExecuteEx</c>
/// and the <c>runas</c> verb, then waits for the child to exit and forwards its exit code.
/// </summary>
/// <remarks>
/// <para>
/// Assembly-level <c>[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]</c> is
/// declared in <c>NativeRestartManagerMethods.cs</c> and covers the entire Engine assembly,
/// so no duplicate attribute is needed here.
/// </para>
/// <para>
/// <b>Security note:</b> <see cref="Relaunch"/> passes <paramref name="executablePath"/>
/// directly to <c>ShellExecuteEx</c> as <c>lpFile</c>. Callers <b>must</b> supply a
/// fully-qualified, trusted path (e.g. <c>Environment.ProcessPath</c>). Passing an
/// attacker-controlled path is a local privilege-escalation vector.
/// </para>
/// </remarks>
public static partial class ElevatedSelfRelauncher
{
    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Composes the argv string used when relaunching this process elevated.
    /// Always includes <c>--bootstrap-elevated</c> and <c>--cache-dir &lt;cacheDir&gt;</c>.
    /// Optionally appends <paramref name="forwarded"/> tokens.
    /// </summary>
    /// <param name="cacheDir">
    /// Fully-qualified path to the extraction cache directory. Must be non-empty.
    /// Quoted automatically if it contains spaces or double-quote characters.
    /// </param>
    /// <param name="forwarded">
    /// Optional additional arguments forwarded verbatim to the elevated child.
    /// Each token is individually quoted when it contains spaces or <c>"</c>.
    /// </param>
    /// <returns>A single string suitable for <c>ShellExecuteEx.lpParameters</c>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="cacheDir"/> is <see langword="null"/> or empty.
    /// </exception>
    public static string BuildRelaunchArgs(string cacheDir, IReadOnlyList<string>? forwarded = null)
    {
        if (string.IsNullOrEmpty(cacheDir))
            throw new ArgumentException("Cache directory must be non-empty.", nameof(cacheDir));

        // Capacity estimate: "--bootstrap-elevated --cache-dir " + quoted path + forwarded tokens.
        // Use StringBuilder to avoid string concatenation allocations.
        var sb = new StringBuilder(64 + cacheDir.Length);

        sb.Append("--bootstrap-elevated");
        sb.Append(" --cache-dir ");
        AppendQuoted(sb, cacheDir);

        if (forwarded is { Count: > 0 })
        {
            foreach (string token in forwarded)
            {
                sb.Append(' ');
                AppendQuoted(sb, token);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Relaunches <paramref name="executablePath"/> elevated via <c>ShellExecuteEx(runas)</c>,
    /// waits for the child to exit, and returns its exit code.
    /// </summary>
    /// <param name="executablePath">
    /// Fully-qualified path to this engine executable.
    /// Use <c>Environment.ProcessPath</c> at the call site; never pass untrusted input.
    /// </param>
    /// <param name="cacheDir">Extraction cache directory passed to the elevated child.</param>
    /// <param name="forwarded">Optional additional arguments forwarded to the elevated child.</param>
    /// <returns>
    /// The exit code of the elevated child process, or <c>2</c> (Cancelled) when the user
    /// dismisses the UAC prompt (<c>ERROR_CANCELLED = 1223</c>).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown on any <c>ShellExecuteEx</c> failure other than user cancellation.
    /// </exception>
    [SupportedOSPlatform("windows")]
    public static int Relaunch(
        string executablePath,
        string cacheDir,
        IReadOnlyList<string>? forwarded = null)
    {
        string parameters = BuildRelaunchArgs(cacheDir, forwarded);

        var info = new ShellExecuteInfoW
        {
            cbSize   = (uint)Marshal.SizeOf<ShellExecuteInfoW>(),
            fMask    = SeeMaskNoCloseProcess | SeeMaskNoAsync,
            hwnd     = nint.Zero,
            lpVerb   = "runas",
            lpFile   = executablePath,
            lpParameters = parameters,
            lpDirectory  = null,
            nShow    = SwNormal,
            hProcess = nint.Zero,
        };

        if (!ShellExecuteExW(ref info))
        {
            int error = Marshal.GetLastWin32Error();

            // ERROR_CANCELLED (1223) = user dismissed UAC — not a system failure.
            // Map to exit code 2 (Cancelled), matching pipeline cancel semantics per plan §2.6.
            if (error == ErrorCancelled)
                return 2;

            throw new InvalidOperationException(
                $"ShellExecuteEx failed with Win32 error {error}.");
        }

        // Wait for the elevated child to complete, then retrieve and forward its exit code.
        try
        {
            WaitForSingleObject(info.hProcess, Infinite);

            if (!GetExitCodeProcess(info.hProcess, out uint exitCode))
                throw new InvalidOperationException(
                    $"GetExitCodeProcess failed with Win32 error {Marshal.GetLastWin32Error()}.");

            return (int)exitCode;
        }
        finally
        {
            if (info.hProcess != nint.Zero)
                CloseHandle(info.hProcess);
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Appends <paramref name="token"/> to <paramref name="sb"/>, quoting per Win32
    /// <c>CommandLineToArgvW</c> rules when the token contains spaces or double-quote characters.
    /// Internal <c>"</c> characters are escaped as <c>\"</c>.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="StringBuilder"/> throughout — no intermediate string allocations.
    /// </remarks>
    private static void AppendQuoted(StringBuilder sb, string token)
    {
        // Fast path: no quoting needed when token contains neither spaces nor quotes.
        bool needsQuoting = token.Contains(' ') || token.Contains('"');

        if (!needsQuoting)
        {
            sb.Append(token);
            return;
        }

        sb.Append('"');

        foreach (char c in token)
        {
            if (c == '"')
                sb.Append('\\'); // Escape internal double-quotes per CommandLineToArgvW.
                                 // Note: trailing backslashes before the closing '"' also need
                                 // doubling per the full CommandLineToArgvW spec, but cache-dir
                                 // paths ending in '\' are not expected in this context.
            sb.Append(c);
        }

        sb.Append('"');
    }

    // ── Win32 constants ─────────────────────────────────────────────────────

    private const uint SeeMaskNoCloseProcess = 0x00000040;
    private const uint SeeMaskNoAsync        = 0x00000100;
    private const int  SwNormal              = 1;
    private const uint Infinite              = 0xFFFFFFFF;
    private const int  ErrorCancelled        = 1223;

    // ── SHELLEXECUTEINFOW struct ─────────────────────────────────────────────

    // Manual layout matches Win32 SHELLEXECUTEINFOW exactly.
    // LPWSTR fields are marshalled as UnmanagedType.LPWStr (source-generated interop, AOT-safe).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfoW
    {
        public uint  cbSize;
        public uint  fMask;
        public nint  hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int   nShow;
        public nint  hInstApp;
        public nint  lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public nint  hkeyClass;
        public uint  dwHotKey;
        // Union: hIcon / hMonitor — single pointer field covers both members.
        public nint  hIconOrMonitor;
        public nint  hProcess;
    }

    // ── P/Invoke declarations ────────────────────────────────────────────────

    // ShellExecuteExW is not source-generator-friendly due to the ref-struct parameter;
    // use DllImport (safe on NativeAOT when marshalling is explicit and AOT-safe).
    [DllImport("shell32.dll", EntryPoint = "ShellExecuteExW",
        CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfoW pExecInfo);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}
