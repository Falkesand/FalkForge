using FalkInstaller.Cli.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace FalkInstaller.Cli.Tests;

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

        var result = command.Execute(CreateContext(), settings);

        Assert.Equal(ExitCodes.RuntimeError, result);
        Assert.Contains(console.Errors, e => e.Contains("File not found"));
    }

    [Fact]
    public void Execute_NonExistentFile_WritesErrorMessage()
    {
        var console = new TestConsoleOutput();
        var command = new BuildCommand(console);
        var settings = new Settings.BuildSettings { ProjectPath = "missing.cs" };

        command.Execute(CreateContext(), settings);

        Assert.Single(console.Errors);
        Assert.Contains("File not found", console.Errors[0]);
    }
}
