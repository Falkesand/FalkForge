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
    /// The user cancelled the operation (cancellation token triggered mid-install).
    /// The parent process should <c>Environment.Exit(2)</c>.
    /// </summary>
    ExitCancelled,

    /// <summary>
    /// Either a prerequisite installer exited with a non-zero failure code, or prerequisites
    /// are missing and this process is not elevated (so it cannot install them itself). The
    /// parent process should <c>Environment.Exit(1)</c>. In the latter case
    /// <see cref="PreUIBootstrapResult.MissingPrerequisiteNames"/> names what is missing.
    /// </summary>
    ExitFailed,

    /// <summary>
    /// A prerequisite installer requested a system reboot (exit code 3010 or 1641 with block behaviour).
    /// The parent process should <c>Environment.Exit(3)</c>.
    /// </summary>
    ExitRebootRequired,
}
