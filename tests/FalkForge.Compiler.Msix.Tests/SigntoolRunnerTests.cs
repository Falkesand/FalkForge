using System.Diagnostics;
using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests;

[SupportedOSPlatform("windows")]
public sealed class SigntoolRunnerTests
{
    // Regression test for the ignored WaitForExit(TimeSpan) return value: when the child
    // process outlives the timeout, WaitForExit returns false. The old code (duplicated in
    // MsixCompiler.SignPackage and MsixBundleCompiler.SignBundle) ignored that return value,
    // fell through, and read process.ExitCode on a still-running process while `using`
    // disposed the Process handle underneath it — leaking the child instead of killing it.
    [Fact]
    public async Task Run_ProcessOutlivesTimeout_KillsProcessTreeAndReportsTimeout()
    {
        var pingCountBefore = Process.GetProcessesByName("PING").Length;

        // "ping -n 60" runs for roughly a minute — far longer than the timeout below —
        // so the only way this call returns quickly is if the runner kills it.
        var result = SigntoolRunner.Run("cmd.exe", "/c ping -n 60 127.0.0.1", TimeSpan.FromMilliseconds(300));

        Assert.True(result.IsFailure);
        Assert.Contains("timed out", result.Error.Message, StringComparison.OrdinalIgnoreCase);

        // Give the OS a moment to finish tearing down the killed tree, then confirm no
        // orphaned ping.exe survives this call — that is the leak the bug caused.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        var pingCountAfter = Process.GetProcessesByName("PING").Length;

        Assert.Equal(pingCountBefore, pingCountAfter);
    }
}
