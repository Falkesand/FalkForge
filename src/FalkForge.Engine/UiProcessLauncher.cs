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
    /// <summary>
    /// Builds the child's <see cref="ProcessStartInfo"/>. Separate from the launch so a test can
    /// read back what the engine asks the OS for without starting anything.
    /// </summary>
    internal static ProcessStartInfo BuildStartInfo(string uiExePath, string uiArgs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = uiExePath,
            Arguments = uiArgs,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        // The UI is a WinExe. When the .NET host cannot start it — no Desktop Runtime installed, or
        // one whose major version does not match — the host's default behaviour is a modal error
        // dialog, and the process then never exits. Measured on a machine with a healthy SDK: both
        // of those cases sat for the full 20 s the measurement allowed and had to be killed. With
        // this variable set, the same two cases exit in under 30 ms (0x80008083 for a missing
        // runtime, 0x80008096 for a wrong or partial one) and print the host's own explanation.
        //
        // The engine cannot report a failure it is still waiting on, so this is what makes the
        // failure reportable at all. It is harmless when the runtime is healthy: the UI came up
        // normally with it set.
        //
        // Nothing is redirected. With UseShellExecute=false the child inherits the engine's own
        // stdout and stderr, so the host's explanation lands on the same stream as the engine's
        // message, in order, without the engine owning a pipe it would have to drain for the whole
        // life of a healthy UI. Measured: the full "You must install .NET to run this application"
        // block appeared in the parent's stderr between the parent's own two lines.
        startInfo.Environment["DOTNET_DISABLE_GUI_ERRORS"] = "1";

        return startInfo;
    }

    internal static Result<Process> TryStartUiProcess(string uiExePath, string uiArgs)
    {
        try
        {
            var process = Process.Start(BuildStartInfo(uiExePath, uiArgs));

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
