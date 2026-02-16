using FalkForge.Cli.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class DecompileCommandTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "decompile", null);

    [Fact]
    public void Execute_NonExistentFile_ReturnsRuntimeError()
    {
        var console = new TestConsoleOutput();
        var command = new DecompileCommand(console);
        var settings = new Settings.DecompileSettings { MsiPath = "nonexistent_file_xyz.msi" };

        var result = command.Execute(CreateContext(), settings);

        Assert.Equal(ExitCodes.RuntimeError, result);
    }

    [Fact]
    public void Execute_NonExistentFile_WritesErrorMessage()
    {
        var console = new TestConsoleOutput();
        var command = new DecompileCommand(console);
        var settings = new Settings.DecompileSettings { MsiPath = "missing.msi" };

        command.Execute(CreateContext(), settings);

        Assert.Contains(console.Errors, e => e.Contains("File not found"));
    }

    [Fact]
    public void Execute_OutputPathDefault_IsNull()
    {
        var settings = new Settings.DecompileSettings { MsiPath = "test.msi" };

        Assert.Null(settings.OutputPath);
    }
}
