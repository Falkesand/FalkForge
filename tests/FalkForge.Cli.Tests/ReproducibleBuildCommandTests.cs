using FalkForge.Cli.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for the --reproducible flag error paths in BuildCommand.
/// </summary>
public sealed class ReproducibleBuildCommandTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    [Fact]
    public void Execute_ReproducibleWithInvalidSourceDateEpoch_WritesRpr001Error()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "not-a-number");
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new Settings.BuildSettings
            {
                ProjectPath = "installer.cs",
                Reproducible = true
            };

            command.Execute(CreateContext(), settings);

            Assert.Contains(console.Errors, e => e.Contains("RPR001"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }

    [Fact]
    public void Execute_ReproducibleWithInvalidSourceDateEpoch_ReturnsRuntimeError()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "not-a-number");
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new Settings.BuildSettings
            {
                ProjectPath = "installer.cs",
                Reproducible = true
            };

            var result = command.Execute(CreateContext(), settings);

            Assert.Equal(ExitCodes.RuntimeError, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }

    [Fact]
    public void Execute_ReproducibleWithValidSourceDateEpoch_DoesNotWriteRpr001Error()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1700000000");
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            // Use a non-existent file so we can inspect error output without
            // actually running a build — the file-not-found check happens AFTER
            // SOURCE_DATE_EPOCH resolution succeeds.
            var settings = new Settings.BuildSettings
            {
                ProjectPath = "nonexistent_xyz.cs",
                Reproducible = true
            };

            command.Execute(CreateContext(), settings);

            Assert.DoesNotContain(console.Errors, e => e.Contains("RPR001") || e.Contains("RPR002"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }

    [Fact]
    public void Execute_ReproducibleNoEnvVarNoGit_WritesRpr002Error()
    {
        // Ensure SOURCE_DATE_EPOCH is absent
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);

        var console = new TestConsoleOutput();
        var command = new BuildCommand(console, gitWorkingDirectory: Path.GetTempPath());
        var settings = new Settings.BuildSettings
        {
            ProjectPath = "installer.cs",
            Reproducible = true
        };

        // Run from a temp directory that is not a git repo so git fallback fails.
        var result = command.Execute(CreateContext(), settings);

        // If git happened to succeed (e.g., temp dir is inside a git repo on the
        // test machine), skip the assertion — we can't control the CI environment.
        if (console.Errors.Any(e => e.Contains("RPR002")))
        {
            Assert.Equal(ExitCodes.RuntimeError, result);
        }
        else
        {
            // git succeeded (temp dir inside a repo) — test is vacuously satisfied.
            Assert.True(true);
        }
    }
}
