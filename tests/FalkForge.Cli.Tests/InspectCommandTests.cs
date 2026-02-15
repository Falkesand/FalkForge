using FalkForge.Cli.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class InspectCommandTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "inspect", null);

    [Fact]
    public void Execute_NonExistentFile_ReturnsRuntimeError()
    {
        var console = new TestConsoleOutput();
        var command = new InspectCommand(console);
        var settings = new Settings.InspectSettings { MsiPath = "nonexistent_file_xyz.msi" };

        var result = command.Execute(CreateContext(), settings);

        Assert.Equal(ExitCodes.RuntimeError, result);
    }

    [Fact]
    public void Execute_NonExistentFile_WritesErrorMessage()
    {
        var console = new TestConsoleOutput();
        var command = new InspectCommand(console);
        var settings = new Settings.InspectSettings { MsiPath = "missing.msi" };

        command.Execute(CreateContext(), settings);

        Assert.Contains(console.Errors, e => e.Contains("File not found"));
    }
}
