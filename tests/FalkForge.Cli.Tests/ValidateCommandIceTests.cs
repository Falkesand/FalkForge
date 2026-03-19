using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class ValidateCommandIceTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "validate", null);

    [Fact]
    public void Execute_MsiWithoutIceFlag_ShowsHint()
    {
        var console = new TestConsoleOutput();
        var command = new ValidateCommand(console);

        var tempMsi = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.msi");
        File.WriteAllBytes(tempMsi, [0]);
        try
        {
            var settings = new ValidateSettings { ProjectPath = tempMsi, Ice = false };
            var result = command.Execute(CreateContext(), settings);

            Assert.Equal(ExitCodes.Success, result);
            Assert.Contains(console.MarkupLines, l => l.Contains("--ice"));
        }
        finally
        {
            File.Delete(tempMsi);
        }
    }

    [Fact]
    public void Execute_CsxIgnoresIceFlag()
    {
        var console = new TestConsoleOutput();
        var command = new ValidateCommand(console);

        // .csx file that doesn't exist — should hit "File not found", proving
        // the command took the normal script-loading path, not the ICE path.
        var settings = new ValidateSettings { ProjectPath = "nonexistent.csx", Ice = true };
        var result = command.Execute(CreateContext(), settings);

        Assert.Equal(ExitCodes.RuntimeError, result);
        Assert.Contains(console.Errors, e => e.Contains("File not found"));
    }
}
