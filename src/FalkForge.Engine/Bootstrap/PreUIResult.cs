namespace FalkForge.Engine.Bootstrap;

using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Discriminated union returned by <see cref="PreUIPrerequisiteInstaller.RunAllAsync"/>.
/// All variants are AOT-safe sealed records.
/// </summary>
public abstract record PreUIResult
{
    // Sealed constructor prevents external subclassing while keeping the hierarchy exhaustive.
    private PreUIResult() { }

    /// <summary>All pre-UI prerequisites installed successfully.</summary>
    public sealed record Success : PreUIResult;

    /// <summary>Installation was cancelled via <see cref="System.Threading.CancellationToken"/>.</summary>
    public sealed record Cancelled : PreUIResult;

    /// <summary>
    /// A prerequisite exited with a non-zero, non-reboot exit code.
    /// Subsequent prerequisites were NOT run.
    /// </summary>
    /// <param name="Package">The prerequisite that failed.</param>
    /// <param name="ExitCode">The exit code returned by the child process.</param>
    public sealed record Failed(PreUIPackageInfo Package, int ExitCode) : PreUIResult;

    /// <summary>
    /// A prerequisite exited with 3010 or 1641 and its <see cref="PreUIRebootBehavior"/>
    /// was <see cref="PreUIRebootBehavior.Block"/> (or 1641 — always blocks).
    /// Subsequent prerequisites were NOT run.
    /// </summary>
    /// <param name="Package">The prerequisite requesting a reboot.</param>
    /// <param name="ExitCode">3010 or 1641.</param>
    public sealed record RebootRequired(PreUIPackageInfo Package, int ExitCode) : PreUIResult;
}
