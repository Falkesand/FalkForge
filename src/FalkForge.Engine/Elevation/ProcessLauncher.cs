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
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                Verb = "runas",
                UseShellExecute = true,
            };

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
}
