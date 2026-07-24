namespace FalkForge.Engine.Bootstrap;

/// <summary>
/// Describes the decision <see cref="PreUIBootstrapOrchestrator"/> communicates back to
/// <c>BootstrapperRunner.RunAsync</c> after the pre-UI prerequisite phase completes.
/// </summary>
public enum PreUIBootstrapOutcome
{
    /// <summary>
    /// Pre-UI phase completed (or was not needed). The parent process should proceed to
    /// launch the UI executable.
    /// </summary>
    LaunchUi,

    /// <summary>
    /// This process is the elevated child and all prerequisites were installed successfully.
    /// The parent process should <c>Environment.Exit(0)</c> — do NOT launch the UI.
    /// The unelevated parent will continue to the UI launch upon receiving exit code 0.
    /// </summary>
    ExitSuccess,

    /// <summary>
    /// The user cancelled the operation (UAC dismissal or cancellation token).
    /// The parent process should <c>Environment.Exit(2)</c>.
    /// </summary>
    ExitCancelled,

    /// <summary>
    /// A prerequisite installer exited with a non-zero failure code.
    /// The parent process should <c>Environment.Exit(1)</c>.
    /// </summary>
    ExitFailed,

    /// <summary>
    /// A prerequisite installer requested a system reboot (exit code 3010 or 1641 with block behaviour).
    /// The parent process should <c>Environment.Exit(3)</c>.
    /// </summary>
    ExitRebootRequired,
}
