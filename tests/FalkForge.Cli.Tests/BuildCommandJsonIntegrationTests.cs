using System.Runtime.Versioning;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Integration tests covering the <c>forge build &lt;fixture&gt;.json</c> path.
/// These tests fail until <see cref="BuildCommand"/> routes JSON input through
/// <c>JsonConfigLoader</c> and <c>MsiCompiler</c> to produce a real MSI.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("SourceDateEpoch")]
public sealed class BuildCommandJsonIntegrationTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    [Fact]
    public void Execute_ValidJsonFixture_ProducesMsiOnDisk()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var tempDir = Path.Combine(Path.GetTempPath(), $"ForgeBuildJson_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var payloadDir = Path.Combine(tempDir, "payload");
            Directory.CreateDirectory(payloadDir);
            var payloadFile = Path.Combine(payloadDir, "app.exe");
            File.WriteAllText(payloadFile, "fixture payload bytes");

            var jsonPath = Path.Combine(tempDir, "installer.json");
            var json = $$"""
            {
                "product": {
                    "name": "JsonFixtureApp",
                    "manufacturer": "TestCorp",
                    "version": "1.0.0",
                    "upgradeCode": "11111111-2222-3333-4444-555555555555"
                },
                "installDirectory": "TestCorp\\JsonFixtureApp",
                "features": [
                    {
                        "id": "Main",
                        "title": "Main Feature",
                        "default": true,
                        "files": [
                            { "source": "payload/app.exe" }
                        ]
                    }
                ]
            }
            """;
            File.WriteAllText(jsonPath, json);

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new BuildSettings
            {
                ProjectPath = jsonPath,
                OutputPath = outputDir,
            };

            var result = command.Execute(CreateContext(), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, result);
            var msiFiles = Directory.GetFiles(outputDir, "*.msi");
            Assert.NotEmpty(msiFiles);
            Assert.True(new FileInfo(msiFiles[0]).Length > 0, "MSI file is empty");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
