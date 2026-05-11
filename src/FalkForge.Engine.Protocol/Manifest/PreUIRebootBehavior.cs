namespace FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Defines the engine's response when a pre-UI prerequisite installer exits with
/// exit code 3010 (reboot required) or 1641 (reboot initiated).
/// </summary>
public enum PreUIRebootBehavior
{
    /// <summary>
    /// Log a warning and continue to the next prerequisite and then UI launch.
    /// Appropriate when the runtime is functional before reboot (e.g., .NET Desktop Runtime).
    /// This is the default.
    /// </summary>
    IgnoreAndContinue = 0,

    /// <summary>
    /// Show a TaskDialog asking the user whether to reboot now or continue.
    /// </summary>
    Prompt = 1,

    /// <summary>
    /// Stop processing, do not launch the UI, and exit with code 3 (RebootRequired).
    /// Use when the prereq is non-functional until rebooted.
    /// </summary>
    Block = 2
}
