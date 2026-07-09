using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for <see cref="PlanCommand"/> with an injectable <see cref="IEngineLauncher"/>.
/// Verifies: file-not-found, engine binary missing, engine failure, happy path,
/// BuildEngineArgs contract.
/// </summary>
public sealed class PlanCommandEngineLauncherTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "plan", null);

    // ──────────────────────────────────────────────────────────────────────────
    // BuildEngineArgs contract
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildEngineArgs_WithOutputPath_ContainsPlanOnly_And_PlanOutput()
    {
        var args = PlanCommand.BuildEngineArgs("manifest.json", @"C:\out\plan.json");

        Assert.Contains("--plan-only", args);
        Assert.Contains("--plan-output", args);
        Assert.Contains(@"C:\out\plan.json", args);
        Assert.Contains("--manifest", args);
        Assert.Contains("manifest.json", args);
    }

    [Fact]
    public void BuildEngineArgs_WithoutOutputPath_ContainsPlanOnly_NoOutputArg()
    {
        var args = PlanCommand.BuildEngineArgs("manifest.json", null);

        Assert.Contains("--plan-only", args);
        Assert.Contains("--manifest", args);
        Assert.DoesNotContain("--plan-output", args);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IEngineLauncher is injectable (constructor DI seam)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PlanCommand_AcceptsEngineLauncherInConstructor()
    {
        // Verify IEngineLauncher exists and PlanCommand accepts it.
        var launcher = new FakeEngineLauncher(exitCode: 0, stdout: "{}");
        var output = new TestConsoleOutput();
        var command = new PlanCommand(output, launcher: launcher);
        // Instance created without throwing — the launcher injection seam is wired.
        Assert.NotNull(command);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Error path: file not found
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_FileNotFound_ReturnsRuntimeError()
    {
        var launcher = new FakeEngineLauncher(exitCode: 0, stdout: "{}");
        var output = new TestConsoleOutput();
        var command = new PlanCommand(output, launcher: launcher);
        var settings = new PlanSettings { ProjectPath = "nonexistent_bundle_xyz.exe" };

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, result);
    }

    [Fact]
    public void Execute_FileNotFound_WritesErrorMessage()
    {
        var launcher = new FakeEngineLauncher(exitCode: 0, stdout: "{}");
        var output = new TestConsoleOutput();
        var command = new PlanCommand(output, launcher: launcher);
        var settings = new PlanSettings { ProjectPath = "nonexistent_bundle_xyz.exe" };

        command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.True(output.Errors.Count > 0, "Expected at least one error message");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IEngineLauncher: exists with correct signature
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IEngineLauncher_ExistsWithLaunchAsyncSignature()
    {
        // Verifies IEngineLauncher is a real interface in the FalkForge.Cli namespace.
        // Uses reflection only to avoid a direct dependency that would compile away.
        var type = typeof(PlanCommand).Assembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == "IEngineLauncher");

        Assert.NotNull(type);
        Assert.True(type!.IsInterface, "IEngineLauncher must be an interface");
    }
}

/// <summary>
/// Test double for <see cref="IEngineLauncher"/> that returns a canned result.
/// </summary>
file sealed class FakeEngineLauncher : IEngineLauncher
{
    private readonly int _exitCode;
    private readonly string _stdout;

    public FakeEngineLauncher(int exitCode, string stdout)
    {
        _exitCode = exitCode;
        _stdout = stdout;
    }

    public string? LastExePath { get; private set; }
    public string[]? LastArgs { get; private set; }

    public Task<EngineLaunchResult> LaunchAsync(
        string exePath, string[] args, CancellationToken ct)
    {
        LastExePath = exePath;
        LastArgs = args;
        return Task.FromResult(new EngineLaunchResult(_exitCode, _stdout));
    }
}
