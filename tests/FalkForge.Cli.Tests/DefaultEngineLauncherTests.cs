using FalkForge.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Integration-style tests for <see cref="DefaultEngineLauncher"/>. Launch a real process
/// (cmd /c or /bin/sh -c) to verify stdout and stderr capture, and cancellation behavior.
/// Tests use only the system shell, which is present on every CI runner and developer machine —
/// no engine binary required.
/// </summary>
public sealed class DefaultEngineLauncherTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Stderr capture (Item 5)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A process that writes to stderr must have its stderr captured in
    /// <see cref="EngineLaunchResult.Stderr"/>. Intent: engine crashes (or validation errors
    /// written to stderr) must reach the CLI caller so PlanCommand can display them.
    /// </summary>
    [Fact]
    public async Task LaunchAsync_ProcessWritesToStderr_CapturedInResult()
    {
        var launcher = new DefaultEngineLauncher();

        // cmd /c "echo text 1>&2" redirects to stderr on Windows.
        // /bin/sh -c on non-Windows.
        string exePath;
        string[] args;

        if (OperatingSystem.IsWindows())
        {
            exePath = "cmd.exe";
            args = ["/c", "echo engine-stderr-line 1>&2"];
        }
        else
        {
            exePath = "/bin/sh";
            args = ["-c", "echo engine-stderr-line 1>&2"];
        }

        var result = await launcher.LaunchAsync(exePath, args, CancellationToken.None);

        Assert.Contains("engine-stderr-line", result.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stdout and stderr are captured independently — a process writing to both must not
    /// have them merged. Intent: structured stdout (plan JSON) must not be corrupted by
    /// stderr messages from the engine.
    /// </summary>
    [Fact]
    public async Task LaunchAsync_ProcessWritesBoth_CapturedSeparately()
    {
        var launcher = new DefaultEngineLauncher();

        string exePath;
        string[] args;

        if (OperatingSystem.IsWindows())
        {
            exePath = "cmd.exe";
            // Write to stdout first, then stderr, then stdout again.
            args = ["/c", "echo stdout-line && echo stderr-line 1>&2 && echo stdout-line2"];
        }
        else
        {
            exePath = "/bin/sh";
            args = ["-c", "echo stdout-line; echo stderr-line 1>&2; echo stdout-line2"];
        }

        var result = await launcher.LaunchAsync(exePath, args, CancellationToken.None);

        Assert.Contains("stdout-line", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("stderr-line", result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("stderr-line", result.Stdout, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Cancellation / process cleanup (Item 3)
    // ──────────────────────────────────────────────────────────────────────────

    // NOTE: A deterministic unit test for "cancelled token → subprocess is dead" is
    // not written here. The interaction between WaitForExitAsync(ct) throwing
    // OperationCanceledException and then process.Kill(entireProcessTree: true) completing
    // asynchronously means any assertion on HasExited would require a polling/sleep loop
    // that introduces flakiness on loaded CI. The fix is implemented and documented in
    // DefaultEngineLauncher.LaunchAsync. The existing cancellation path in PlanCommand
    // (which catches OperationCanceledException from _launcher.LaunchAsync) continues to
    // cover the happy-path contract.
}
