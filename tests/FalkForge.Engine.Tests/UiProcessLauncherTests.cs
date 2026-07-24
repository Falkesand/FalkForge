namespace FalkForge.Engine.Tests;

using FalkForge.Engine;
using Xunit;

/// <summary>
/// Verifies <see cref="UiProcessLauncher.TryStartUiProcess"/> — the helper extracted from
/// <c>BootstrapperRunner.RunAsync</c> so the UI-launch failure path is unit-testable without
/// spawning a full bootstrap. The real bug this guards: <see cref="System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)"/>
/// can THROW (e.g. <see cref="System.ComponentModel.Win32Exception"/> for a nonexistent or
/// non-executable path) rather than returning null. Before this helper existed,
/// <c>BootstrapperRunner</c> only handled the null-return case, so a thrown exception skipped
/// the cleanup (disposing <c>initPipe</c>) and crashed the bootstrapper instead of failing
/// clean. <c>uiExePath</c> comes from the bundle's extracted manifest, so a corrupt or
/// tampered bundle can trigger this path.
/// </summary>
public sealed class UiProcessLauncherTests
{
    [Fact]
    public void TryStartUiProcess_NonexistentPath_ReturnsFailure_DoesNotThrow()
    {
        // Intent: a corrupt/tampered bundle can point uiExePath at a path that does not exist
        // or is not executable. Process.Start throws Win32Exception in that case rather than
        // returning null — the helper's whole job is to convert that throw into a Result
        // failure so the caller's existing "process is null" handling can catch both cases
        // uniformly. If this regresses to a raw Process.Start call, the exception escapes and
        // this test fails by crashing instead of asserting.
        var tempDir = Directory.CreateTempSubdirectory("falkforge-ui-launch-test-");
        try
        {
            var bogusPath = Path.Combine(tempDir.FullName, "does-not-exist-ui.exe");

            var result = UiProcessLauncher.TryStartUiProcess(bogusPath, uiArgs: string.Empty);

            Assert.True(result.IsFailure, "launching a nonexistent UI executable must fail, not throw");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void TryStartUiProcess_ValidExecutable_ReturnsSuccess()
    {
        // Intent: the happy path must keep working — the helper is not allowed to turn a
        // legitimate UI launch into a failure. Uses ComSpec (cmd.exe), always present on
        // Windows CI/dev boxes, with an argument that exits immediately so nothing lingers.
        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        Assert.False(string.IsNullOrEmpty(comSpec), "ComSpec must be set on Windows");

        var result = UiProcessLauncher.TryStartUiProcess(comSpec!, "/c exit 0");

        Assert.True(result.IsSuccess, $"expected success, got failure: {(result.IsFailure ? result.Error.Message : string.Empty)}");
        using var process = result.Value;
        process.WaitForExit(5000);
    }
}
