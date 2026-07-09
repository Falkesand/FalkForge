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
        var settings = new Settings.DecompileSettings { FilePath = "nonexistent_file_xyz.msi" };

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, result);
    }

    [Fact]
    public void Execute_NonExistentFile_WritesErrorMessage()
    {
        var console = new TestConsoleOutput();
        var command = new DecompileCommand(console);
        var settings = new Settings.DecompileSettings { FilePath = "missing.msi" };

        command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.Contains(console.Errors, e => e.Contains("File not found"));
    }

    [Fact]
    public void Execute_OutputPathDefault_IsNull()
    {
        var settings = new Settings.DecompileSettings { FilePath = "test.msi" };

        Assert.Null(settings.OutputPath);
    }
}
