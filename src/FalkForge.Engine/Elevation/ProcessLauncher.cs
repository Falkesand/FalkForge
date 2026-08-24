namespace FalkForge.Engine.Elevation;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal sealed class ProcessLauncher : IProcessLauncher
{
    public Result<Process> Launch(string exePath, string arguments)
    {
        try
        {
            var startInfo = BuildStartInfo(exePath, arguments);

            var process = Process.Start(startInfo);
            if (process is null)
                return Result<Process>.Failure(ErrorKind.ElevationError, "Failed to start elevated process.");

            return Result<Process>.Success(process);
        }
        catch (Win32Exception)
        {
            return Result<Process>.Failure(ErrorKind.ElevationError, "Elevation was cancelled by the user.");
        }
        catch (Exception ex)
        {
            return Result<Process>.Failure(ErrorKind.ElevationError, $"Failed to launch elevated process: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the <see cref="ProcessStartInfo"/> used to launch the elevated companion.
    /// Deliberately does not set <c>Verb</c>: the companion carries its own
    /// <c>requireAdministrator</c> manifest, so the shell elevates it on the default verb.
    /// Requesting the "runas" verb instead would let a same-user attacker redirect the
    /// elevated launch by overriding <c>HKCU\Software\Classes\exefile\shell\runas\command</c>.
    /// Internal so tests can assert on the constructed info without spawning a process.
    /// </summary>
    internal static ProcessStartInfo BuildStartInfo(string exePath, string arguments)
        => new()
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = true,
        };
}
