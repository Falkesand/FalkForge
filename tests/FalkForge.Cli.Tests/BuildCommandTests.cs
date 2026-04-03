using FalkForge.Cli.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

[Collection("SourceDateEpoch")]
public sealed class BuildCommandTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    [Fact]
    public void Execute_NonExistentFile_ReturnsRuntimeError()
    {
        var console = new TestConsoleOutput();
        var command = new BuildCommand(console);
        var settings = new Settings.BuildSettings { ProjectPath = "nonexistent_file_xyz.cs" };

        var result = command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, result);
        Assert.Contains(console.Errors, e => e.Contains("File not found"));
    }

    [Fact]
    public void Execute_NonExistentFile_WritesErrorMessage()
    {
        var console = new TestConsoleOutput();
        var command = new BuildCommand(console);
        var settings = new Settings.BuildSettings { ProjectPath = "missing.cs" };

        command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Single(console.Errors);
        Assert.Contains("File not found", console.Errors[0]);
    }

    // ── ResolveSourceDateEpoch tests ──

    [Fact]
    public void ResolveSourceDateEpoch_ValidEnvVar_ReturnsValue()
    {
        var prior = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1700000000");
        try
        {
            var console = new TestConsoleOutput();

            var result = BuildCommand.ResolveSourceDateEpoch(console);

            Assert.Equal(1700000000L, result);
            Assert.Empty(console.Errors);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", prior);
        }
    }

    [Fact]
    public void ResolveSourceDateEpoch_InvalidEnvVar_ReturnsNullAndWritesError()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "not-a-number");
        try
        {
            var console = new TestConsoleOutput();

            var result = BuildCommand.ResolveSourceDateEpoch(console);

            Assert.Null(result);
            Assert.Single(console.Errors);
            Assert.Contains("RPR001", console.Errors[0]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }

    [Fact]
    public void ResolveSourceDateEpoch_ZeroValue_ReturnsZero()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "0");
        try
        {
            var console = new TestConsoleOutput();

            var result = BuildCommand.ResolveSourceDateEpoch(console);

            Assert.Equal(0L, result);
            Assert.Empty(console.Errors);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }

    [Fact]
    public void ResolveSourceDateEpoch_NoEnvVar_NoGitRepo_ReturnsNullAndWritesRPR002()
    {
        var prior = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        try
        {
            var console = new TestConsoleOutput();

            // Pass a non-git directory so git fails deterministically
            var result = BuildCommand.ResolveSourceDateEpoch(console, Path.GetTempPath());

            Assert.Null(result);
            Assert.Single(console.Errors);
            Assert.Contains("RPR002", console.Errors[0]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", prior);
        }
    }

    [Fact]
    public void Execute_ReproducibleWithInvalidEnvVar_ReturnsRuntimeError()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "bad-value");
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new Settings.BuildSettings
            {
                ProjectPath = "installer.cs",
                Reproducible = true
            };

            var result = command.Execute(CreateContext(), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.RuntimeError, result);
            Assert.Contains(console.Errors, e => e.Contains("RPR001"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }
}
