using FalkForge.Cli.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class BuildCommandMsixTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    [Fact]
    public void Execute_FormatMsix_ShowsMessage()
    {
        // Arrange: create a real .csx file so the command reaches the format check
        var tempFile = Path.Combine(Path.GetTempPath(), $"msix_test_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempFile, "// empty script");
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new Settings.BuildSettings
            {
                ProjectPath = tempFile,
                Format = "msix"
            };

            // Act
            command.Execute(CreateContext(), settings, CancellationToken.None);

            // Assert
            Assert.Contains(console.MarkupLines,
                line => line.Contains("MSIX compilation from .cs scripts requires calling Installer.BuildMsix()"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Execute_FormatMsixJson_ShowsNotSupported()
    {
        // Arrange: create a minimal valid JSON config file matching expected schema
        var tempFile = Path.Combine(Path.GetTempPath(), $"msix_test_{Guid.NewGuid():N}.json");
        File.WriteAllText(tempFile, """
            {
                "product": {
                    "name": "TestProduct",
                    "manufacturer": "TestManufacturer",
                    "version": "1.0.0"
                },
                "features": [
                    {
                        "id": "Complete",
                        "title": "Complete",
                        "files": []
                    }
                ]
            }
            """);
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new Settings.BuildSettings
            {
                ProjectPath = tempFile,
                Format = "msix"
            };

            // Act
            var result = command.Execute(CreateContext(), settings, CancellationToken.None);

            // Assert
            Assert.Equal(ExitCodes.ValidationFailure, result);
            Assert.Contains(console.Errors,
                e => e.Contains("MSIX packages cannot be built from JSON configuration"));
            Assert.Contains(console.Errors,
                e => e.Contains("Installer.BuildMsix()"));
            Assert.Contains(console.Errors,
                e => e.Contains("demo/15-msix-basic"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
