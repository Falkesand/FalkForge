namespace FalkForge.Engine;

using System.ComponentModel;
using System.Diagnostics;

/// <summary>
/// <see cref="IUiProcessHandle"/> over a real <see cref="Process"/>. The bootstrapper wraps the UI
/// process it just started and hands the wrapper to the session, which is the only thing that
/// carries the process across to the handshake wait.
/// <para>
/// It does not own the process object: <c>BootstrapperRunner</c> created it and disposes it. Every
/// call here tolerates a process that has already gone away, because between any two of them the
/// UI can exit on its own.
/// </para>
/// </summary>
internal sealed class SystemUiProcessHandle(Process process) : IUiProcessHandle
{
    private readonly Process _process = process;

    public int ProcessId { get; } = process.Id;

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // No process is associated with the object any more; treat that as gone.
                return true;
            }
        }
    }

    public int ExitCode
    {
        get
        {
            try
            {
                return _process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                // Asked before the process exited, or after the association was dropped. There is
                // no code to report, and the caller renders this as a hexadecimal exit code, so
                // zero reads as "no code" rather than as a wrong one.
                return 0;
            }
        }
    }

    public async Task WaitForExitAsync(CancellationToken ct)
    {
        try
        {
            await _process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The handshake won the race and cancelled this wait. Documented as the healthy path.
        }
        catch (InvalidOperationException)
        {
            // Process object no longer associated with an OS process: it is gone, so the wait is over.
        }
    }

    public void KillTree()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the check and the kill.
        }
        catch (Win32Exception)
        {
            // The OS refused the termination (already terminating, or access denied). Nothing
            // further this side can do, and the engine is about to report the handshake failure
            // regardless — failing to kill must not replace that message with an exception.
        }
    }
}
