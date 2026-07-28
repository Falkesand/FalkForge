using FalkForge.Cli.Verification;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Exercises the REAL <see cref="DefaultRebuildRunner.RebuildAsync"/> — spawning an actual
/// <c>dotnet run</c> subprocess — rather than <c>FakeRebuildRunner</c> (see
/// <see cref="RebuildRunnerContractTests"/>). Before this file, only the static
/// <see cref="DefaultRebuildRunner.BuildArguments"/> helper had ever run for real; the process
/// spawn, the timeout-vs-cancellation race between the linked <see cref="CancellationTokenSource"/>
/// tokens, and the <c>process.Kill(entireProcessTree: true)</c> cleanup had never executed.
///
/// Both tests point <c>dotnet run</c> at a throwaway console project (created fresh in a temp
/// directory, outside the repo tree so it never picks up this repo's Directory.Build.props) whose
/// <c>Main</c> sleeps far longer than the timeouts used here. That guarantees the subprocess is
/// still alive when the timeout/cancellation fires, without depending on how fast the local
/// machine can resolve/build a trivial project — deterministic regardless of machine speed.
/// </summary>
public sealed class DefaultRebuildRunnerRealProcessTests
{
    private static string CreateSleepingProject(string tempDir)
    {
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "Sleeper.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        // Sleeps far longer than any timeout used by these tests, so the subprocess is
        // guaranteed to still be running when RebuildAsync's timeout/cancellation fires.
        File.WriteAllText(Path.Combine(tempDir, "Program.cs"), "System.Threading.Thread.Sleep(120_000);\n");
        return Path.Combine(tempDir, "Sleeper.csproj");
    }

    [Fact]
    public async Task RebuildAsync_ProcessOutlivesTimeout_ReturnsExitCodeMinusOneWithTimeoutMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"RebuildTimeout_{Guid.NewGuid():N}");
        try
        {
            var projectPath = CreateSleepingProject(tempDir);
            var outputDir = Path.Combine(tempDir, "out");
            Directory.CreateDirectory(outputDir);

            IRebuildRunner runner = new DefaultRebuildRunner();

            var result = await runner.RebuildAsync(
                projectPath,
                outputDir,
                sourceDateEpoch: 1577836800,
                timeout: TimeSpan.FromSeconds(3),
                CancellationToken.None);

            // The timeout branch must synthesize ExitCode -1 rather than throwing, and must not
            // leave the killed process's real exit code (which would be whatever `dotnet run`
            // reports for a SIGKILL'd/terminated tree — never surfaced here).
            Assert.Equal(-1, result.ExitCode);
            Assert.Contains("timed out", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RebuildAsync_CallerCancels_PropagatesCancellationInsteadOfSwallowingIntoResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"RebuildCancel_{Guid.NewGuid():N}");
        try
        {
            var projectPath = CreateSleepingProject(tempDir);
            var outputDir = Path.Combine(tempDir, "out");
            Directory.CreateDirectory(outputDir);

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // caller-side cancellation, distinct from the internal timeout

            IRebuildRunner runner = new DefaultRebuildRunner();

            // A generous timeout (60s) that must NOT be the reason this throws — only the
            // caller's own token being pre-cancelled should trigger the rethrow branch. If the
            // implementation regressed to swallowing caller cancellation into a RebuildResult
            // (the same bug shape as the timeout branch, but for the wrong trigger), this would
            // return a result instead of throwing.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                runner.RebuildAsync(
                    projectPath,
                    outputDir,
                    sourceDateEpoch: 1577836800,
                    timeout: TimeSpan.FromSeconds(60),
                    cts.Token));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
