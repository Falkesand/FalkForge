using FalkForge.Cli.Verification;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Exercises the REAL <see cref="DefaultRebuildRunner.RebuildAsync"/> — spawning an actual
/// <c>dotnet run</c> subprocess — rather than <c>FakeRebuildRunner</c> (see
/// <see cref="RebuildRunnerContractTests"/>). Before this file, only the static
/// <see cref="DefaultRebuildRunner.BuildArguments"/> helper had ever run for real; the process
/// spawn, the timeout-vs-cancellation race between the linked <see cref="CancellationTokenSource"/>
/// tokens, and the <c>process.Kill(entireProcessTree: true)</c> cleanup had never executed. Both
/// tests below assert that the kill actually took effect (see
/// <see cref="AssertKilledProcessTreeReleasedDirectory"/>), not merely that <c>RebuildAsync</c>
/// returned the expected result — deleting the <c>Kill</c> call from production would still leave
/// both tests' top-level assertions passing while leaking a 120s-sleeping <c>dotnet</c> tree.
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

    /// <summary>
    /// Confirms <c>process.Kill(entireProcessTree: true)</c> actually released
    /// <paramref name="tempDir"/>: a still-alive descendant (e.g. the sleeping app's own built
    /// assembly) keeps a file handle open under it, so a recursive delete keeps throwing
    /// <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/>. Retries briefly because
    /// <c>Kill</c> requests termination but does not itself wait for the process to exit and
    /// release its handles. On success this also performs the test's own cleanup, so callers should
    /// treat a successful call as "deleted", not merely "verified".
    /// </summary>
    private static void AssertKilledProcessTreeReleasedDirectory(string tempDir)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException ex)
            {
                last = ex;
                Thread.Sleep(200);
            }
        }

        Assert.Fail(
            $"Directory '{tempDir}' was still locked 3s after RebuildAsync's " +
            $"process.Kill(entireProcessTree: true) returned -- a descendant process likely survived " +
            $"the kill. Last error: {last}");
    }

    /// <summary>
    /// Best-effort backstop cleanup for when an earlier assertion in the test body already failed
    /// (so <see cref="AssertKilledProcessTreeReleasedDirectory"/> never ran) or a test intentionally
    /// leaves the directory behind for that method to inspect. Retries with backoff rather than a
    /// single delete attempt (matches the established pattern in
    /// <c>DemoBuildFixture.Dispose</c>), and swallows the final failure -- this is purely tidy-up,
    /// never the thing a test result should hinge on.
    /// </summary>
    private static void TryDeleteWithRetry(string tempDir)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
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

            // Pin the actual Kill(entireProcessTree: true) behaviour (see class doc) -- also
            // performs this test's cleanup on success.
            AssertKilledProcessTreeReleasedDirectory(tempDir);
        }
        finally
        {
            TryDeleteWithRetry(tempDir);
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
            // Cancel shortly after the process is actually running, rather than before Start() —
            // a pre-cancelled token makes WaitForExitAsync throw within milliseconds of Start(),
            // when the subprocess (and any children `dotnet run` itself spawns) may barely exist
            // yet, so Kill(entireProcessTree: true) can fail against a not-really-started tree and
            // orphan it. CancelAfter guarantees the cancellation hits a genuinely running process,
            // which is also the realistic scenario this branch exists to handle.
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            IRebuildRunner runner = new DefaultRebuildRunner();

            // A generous timeout (60s) that must NOT be the reason this throws — only the
            // caller's own token being cancelled should trigger the rethrow branch. If the
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

            // This branch also calls Kill(entireProcessTree: true) before rethrowing (see
            // DefaultRebuildRunner.RebuildAsync) -- pin that it actually took effect, same as the
            // timeout test above.
            AssertKilledProcessTreeReleasedDirectory(tempDir);
        }
        finally
        {
            TryDeleteWithRetry(tempDir);
        }
    }
}
