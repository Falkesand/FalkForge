using FalkForge.Cli.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class BuildCommandMsixTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    [Fact]
    public void Execute_FormatMsix_FailsLoud_InsteadOfFallingBackToMsi()
    {
        // Arrange: create a real .cs script so the command reaches the format check
        var tempDir = Path.Combine(Path.GetTempPath(), $"msix_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "build.cs");
        File.WriteAllText(tempFile, "// empty script");
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new Settings.BuildSettings
            {
                ProjectPath = tempFile,
                Format = "msix",
                OutputPath = tempDir
            };

            // Act
            var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

            // Assert: non-zero exit, clear message, and — critically — no MSI silently produced
            // in place of the requested MSIX output.
            Assert.Equal(ExitCodes.ValidationFailure, result);
            Assert.Contains(console.Errors,
                e => e.Contains("MSIX packages cannot be built via 'forge build --format msix'"));
            Assert.Contains(console.Errors,
                e => e.Contains("InstallerMsix.BuildMsix()"));
            // The guidance must point at the real build path (a standalone dotnet-script run),
            // not imply forge build itself can execute a BuildMsix()-calling script — its
            // ScriptLoader forces the script to evaluate to a PackageModel, which a BuildMsix()
            // call (returns int) can never satisfy.
            Assert.Contains(console.Errors,
                e => e.Contains("dotnet script"));
            Assert.DoesNotContain(console.MarkupLines, line => line.Contains("Build succeeded"));
            Assert.Empty(Directory.GetFiles(tempDir, "*.msi"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
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
            var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

            // Assert
            Assert.Equal(ExitCodes.ValidationFailure, result);
            Assert.Contains(console.Errors,
                e => e.Contains("MSIX packages cannot be built from JSON configuration"));
            Assert.Contains(console.Errors,
                e => e.Contains("InstallerMsix.BuildMsix()"));
            Assert.Contains(console.Errors,
                e => e.Contains("dotnet script"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
