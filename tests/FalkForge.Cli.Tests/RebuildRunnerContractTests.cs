using FalkForge.Cli.Verification;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Contract tests for the <see cref="IRebuildRunner"/> subprocess seam. The seam mirrors
/// <c>IEngineLauncher</c>: production shells out to <c>dotnet run</c>, while tests inject a
/// fake so <see cref="FalkForge.Cli.Commands.VerifyCommand"/> can be exercised without a real
/// build. These tests pin the interface shape and the argument contract.
/// </summary>
public sealed class RebuildRunnerContractTests
{
    [Fact]
    public void IRebuildRunner_IsInterface_InCliAssembly()
    {
        var type = typeof(ArtifactComparer).Assembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == "IRebuildRunner");

        Assert.NotNull(type);
        Assert.True(type!.IsInterface, "IRebuildRunner must be an interface");
    }

    [Fact]
    public void DefaultRebuildRunner_BuildArguments_RunsProjectIntoOutputDir()
    {
        // The rebuild must invoke the project's own build entry point with -o <dir>, exactly
        // like the demo fixture, so reproducible projects emit into the scratch directory.
        var args = DefaultRebuildRunner.BuildArguments(@"C:\proj\demo.csproj", @"C:\tmp\out");

        Assert.Contains("run", args);
        Assert.Contains("--project", args);
        Assert.Contains(@"C:\proj\demo.csproj", args);
        Assert.Contains("-o", args);
        Assert.Contains(@"C:\tmp\out", args);
    }

    [Fact]
    public async Task FakeRunner_ReturnsCannedResult()
    {
        IRebuildRunner runner = new FakeRebuildRunner(exitCode: 0, stdout: "ok");

        var result = await runner.RebuildAsync(
            "proj.csproj", "out", sourceDateEpoch: 1577836800,
            timeout: TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ok", result.Stdout);
    }
}

file sealed class FakeRebuildRunner : IRebuildRunner
{
    private readonly int _exitCode;
    private readonly string _stdout;

    public FakeRebuildRunner(int exitCode, string stdout)
    {
        _exitCode = exitCode;
        _stdout = stdout;
    }

    public Task<RebuildResult> RebuildAsync(
        string projectPath, string outputDir, long sourceDateEpoch,
        TimeSpan timeout, CancellationToken ct)
        => Task.FromResult(new RebuildResult(_exitCode, _stdout, string.Empty));
}
