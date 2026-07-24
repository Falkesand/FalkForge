namespace FalkForge.Engine;

using System.ComponentModel;
using System.Diagnostics;

/// <summary>
/// Launches the bundle's UI child process. Extracted from <see cref="BootstrapperRunner.RunAsync"/>
/// so the failure path is unit-testable: <see cref="Process.Start(ProcessStartInfo)"/> can THROW
/// (e.g. a nonexistent, inaccessible, or non-executable <c>uiExePath</c>) rather than returning
/// null, and <c>uiExePath</c> comes from the bundle's extracted manifest — a corrupt or tampered
/// bundle can trigger that throw. Both failure shapes (throw and null) are converted to the same
/// <see cref="Result{T}"/> failure so the caller can handle them identically.
/// </summary>
internal static class UiProcessLauncher
{
    internal static Result<Process> TryStartUiProcess(string uiExePath, string uiArgs)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = uiExePath,
                Arguments = uiArgs,
                UseShellExecute = false,
                CreateNoWindow = false
            });

            return process is null
                ? Result<Process>.Failure(ErrorKind.EngineError, "Failed to launch UI process.")
                : Result<Process>.Success(process);
        }
        catch (Win32Exception ex)
        {
            // Thrown when the OS cannot start the process (path does not exist, access denied,
            // or the target is not a valid executable) — the failure mode this helper exists to
            // cover. A corrupt/tampered bundle's uiExePath lands here, not in the null-return path.
            return Result<Process>.Failure(ErrorKind.EngineError, $"Failed to launch UI process: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            // Thrown by Process.Start(ProcessStartInfo) when no file name is set, or when
            // ErrorDialog is requested without an owner window handle. Neither applies to the
            // fixed ProcessStartInfo built above, but the framework contract allows it, so it is
            // caught explicitly rather than relying on a broad catch (Exception).
            return Result<Process>.Failure(ErrorKind.EngineError, $"Failed to launch UI process: {ex.Message}");
        }
    }
}
