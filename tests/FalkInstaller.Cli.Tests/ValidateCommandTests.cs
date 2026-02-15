using FalkInstaller.Cli.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace FalkInstaller.Cli.Tests;

public sealed class ValidateCommandTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "validate", null);

    [Fact]
    public void Execute_NonExistentFile_ReturnsRuntimeError()
    {
        var console = new TestConsoleOutput();
        var command = new ValidateCommand(console);
        var settings = new Settings.ValidateSettings { ProjectPath = "nonexistent_file_xyz.cs" };

        var result = command.Execute(CreateContext(), settings);

        Assert.Equal(ExitCodes.RuntimeError, result);
        Assert.Contains(console.Errors, e => e.Contains("File not found"));
    }

    [Fact]
    public void Execute_NonExistentFile_WritesErrorMessage()
    {
        var console = new TestConsoleOutput();
        var command = new ValidateCommand(console);
        var settings = new Settings.ValidateSettings { ProjectPath = "missing.cs" };

        command.Execute(CreateContext(), settings);

        Assert.Single(console.Errors);
        Assert.Contains("File not found", console.Errors[0]);
    }
}
