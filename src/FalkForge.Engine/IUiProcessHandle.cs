namespace FalkForge.Engine;

/// <summary>
/// The launched UI process, as much of it as the handshake wait needs. The engine starts the UI and
/// then waits for it to connect back over the named pipe; without this the wait cannot tell "the UI
/// died on startup" from "the UI is running and silent", and it cannot stop early when the process
/// is already gone.
/// <para>
/// It is an interface rather than <see cref="System.Diagnostics.Process"/> so a test can drive both
/// shapes without starting a real process, and so <see cref="EngineSessionOptions"/> does not put a
/// disposable OS handle on its public surface.
/// </para>
/// </summary>
public interface IUiProcessHandle
{
    /// <summary>Operating-system id of the UI process, for naming it in a diagnostic message.</summary>
    int ProcessId { get; }

    /// <summary>Whether the process has already exited.</summary>
    bool HasExited { get; }

    /// <summary>
    /// Exit code of the process. Only meaningful once <see cref="HasExited"/> is
    /// <see langword="true"/>.
    /// </summary>
    int ExitCode { get; }

    /// <summary>
    /// Completes when the process exits, or when <paramref name="ct"/> is cancelled, whichever
    /// happens first. It does NOT throw when the token fires — the caller races this against the
    /// pipe handshake and cancels the loser, so a cancellation here is the normal healthy outcome
    /// rather than an error.
    /// </summary>
    Task WaitForExitAsync(CancellationToken ct);

    /// <summary>
    /// Terminates the process and anything it started. Called when the handshake failed, so the UI
    /// does not outlive the engine holding a window or a modal dialog the user cannot act on.
    /// </summary>
    void KillTree();
}
