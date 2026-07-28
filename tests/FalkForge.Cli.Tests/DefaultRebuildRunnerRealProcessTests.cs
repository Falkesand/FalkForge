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
/// tests below assert that the process tree is actually gone afterward (see
/// <see cref="AssertKilledProcessTreeReleasedDirectory"/>: the working directory becomes
/// deletable), not merely that <c>RebuildAsync</c> returned the expected result.
///
/// What that assertion does NOT prove: that <c>entireProcessTree: true</c> specifically is doing
/// the work. Repro outside any test harness (launch <c>dotnet run</c>, kill only the launcher PID
/// via PowerShell <c>Stop-Process</c>, no .NET <c>Kill</c> involved) showed the sleeping child
/// process dies anyway, because <c>dotnet run</c> ties its child's lifetime to itself via a
/// Windows Job Object at the CLI level, independent of anything FalkForge does. Weakening
/// production's <c>Kill(entireProcessTree: true)</c> to a plain <c>Kill()</c> was verified NOT to
/// fail either test here. The <c>entireProcessTree: true</c> flag only matters for descendants
/// outside that job object — e.g. an MSBuild node spawned during the build phase, before the
/// Sleeper app itself starts — and asserting on that phase would reintroduce the timing
/// nondeterminism earlier review rounds already rejected, so it is deliberately not covered here.
/// This directory-deletable inference is also Windows-specific: on POSIX, <c>Directory.Delete</c>
/// can succeed with files still open.
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
    /// Confirms the subprocess tree spawned for <paramref name="tempDir"/> is actually gone: a
    /// still-alive descendant (e.g. the sleeping app's own built assembly) keeps a file handle open
    /// under it, so a recursive delete keeps throwing <see cref="IOException"/>/
    /// <see cref="UnauthorizedAccessException"/> until every descendant has exited. This does NOT
    /// prove <c>entireProcessTree: true</c> specifically is responsible for that teardown — see the
    /// class doc for why (<c>dotnet run</c>'s own Windows Job Object). Retries briefly because
    /// <c>Kill</c> requests termination but does not itself wait for the process to exit and
    /// release its handles. On success this also performs the test's own cleanup, so callers should
    /// treat a successful call as "deleted", not merely "verified".
    /// </summary>
    private static void AssertKilledProcessTreeReleasedDirectory(string tempDir)
    {
        // 30 x 500ms = 15s. Kill() requests termination but does not wait for exit, and antivirus
        // scanning freshly built output can hold file handles open for several seconds -- a tight
        // budget here risks flaking on exactly the kind of machine this assertion exists to guard
        // against, not on a real regression.
        Exception? last = null;
        for (var attempt = 0; attempt < 30; attempt++)
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
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException ex)
            {
                last = ex;
                Thread.Sleep(500);
            }
        }

        Assert.Fail(
            $"Directory '{tempDir}' was still locked 15s after RebuildAsync's " +
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

            // Pin that the process tree is actually gone afterward -- see class doc for what this
            // does and doesn't prove -- also performs this test's cleanup on success.
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
            // orphan it. CancelAfter guarantees the cancellation hits a genuinely running `dotnet`
            // process tree — at 2s the root `dotnet run` process is most likely still restoring or
            // building rather than having reached the Sleeper's own `Thread.Sleep(120_000)`, but
            // that root process being alive and killable is what this branch actually needs, and is
            // also the realistic scenario it exists to handle.
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
            // DefaultRebuildRunner.RebuildAsync) -- pin that the process tree is actually gone
            // afterward, same as the timeout test above (see class doc for what this does and
            // doesn't prove).
            AssertKilledProcessTreeReleasedDirectory(tempDir);
        }
        finally
        {
            TryDeleteWithRetry(tempDir);
        }
    }
}
